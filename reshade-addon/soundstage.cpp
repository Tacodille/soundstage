// Soundstage ReShade add-on (Part 3 proof, grown by Part 11: protocol version 2 adds focus, restore and unload).
//
// Serves a tiny line protocol on the named pipe \\.\pipe\soundstage-reshade so the Soundstage Dalamud plugin can switch the ReShade
// preset, set a uniform and capture the back buffer to a PNG at a path it chooses. The protocol is in docs/proof-reshade.md.
//
// Threading: the pipe thread only parses a line and hands it to the render thread. Every ReShade call is made from the
// reshade_present event, which runs on the thread that owns the effect runtime. The pipe waits up to 3 seconds for the frame.
//
// Built against the ReShade 6.8.0 add-on headers in include/ (API version 20). Copy soundstage.addon64 next to dxgi.dll yourself.

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <reshade.hpp>

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <map>
#include <filesystem>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

extern "C" __declspec(dllexport) const char *NAME = "Soundstage";
extern "C" __declspec(dllexport) const char *DESCRIPTION = "Lets the Soundstage plugin switch presets, set uniforms and capture screenshots over a local named pipe.";

namespace
{
	constexpr const wchar_t *kPipeName = L"\\\\.\\pipe\\soundstage-reshade";
	constexpr int kProtocolVersion = 2;
	constexpr auto kFrameWait = std::chrono::seconds(3);

	std::atomic<reshade::api::effect_runtime *> g_runtime{ nullptr };
	std::atomic<bool> g_stop{ false };
	// Set by "unload": the pipe thread answers it, then drops the current client so the single pipe instance is free again.
	std::atomic<bool> g_drop_client{ false };
	HANDLE g_pipe_thread = nullptr;

	struct Job
	{
		std::string line;
		std::string reply;
		bool done = false;
		bool abandoned = false;
		std::mutex mutex;
		std::condition_variable cv;
	};

	std::mutex g_queue_mutex;
	std::deque<std::shared_ptr<Job>> g_queue;

	void log(reshade::log::level level, const std::string &text)
	{
		const std::string line = "[Soundstage] " + text;
		reshade::log::message(level, line.c_str());
	}

	std::vector<std::string> split(const std::string &line)
	{
		std::vector<std::string> parts;
		std::istringstream stream(line);
		std::string word;
		while (stream >> word)
			parts.push_back(word);
		return parts;
	}

	// The rest of the line after the first word, with leading and trailing spaces removed (a path may contain spaces).
	std::string rest_of(const std::string &line)
	{
		const size_t first_space = line.find(' ');
		if (first_space == std::string::npos)
			return {};
		size_t begin = first_space;
		while (begin < line.size() && line[begin] == ' ')
			++begin;
		size_t end = line.size();
		while (end > begin && (line[end - 1] == ' ' || line[end - 1] == '\r'))
			--end;
		return line.substr(begin, end - begin);
	}

	std::filesystem::path game_folder()
	{
		wchar_t buffer[MAX_PATH * 4] = {};
		const DWORD length = GetModuleFileNameW(nullptr, buffer, static_cast<DWORD>(std::size(buffer)));
		return std::filesystem::path(std::wstring(buffer, length)).parent_path();
	}

	std::filesystem::path from_utf8(const std::string &text)
	{
		if (text.empty())
			return {};
		const int needed = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
		std::wstring wide(static_cast<size_t>(needed), L'\0');
		MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), needed);
		return std::filesystem::path(wide);
	}

	// ---- PNG writer: uncompressed (stored) deflate blocks, so no zlib is needed. ----

	uint32_t crc32_update(uint32_t crc, const uint8_t *data, size_t size)
	{
		static uint32_t table[256];
		static bool ready = false;
		if (!ready)
		{
			for (uint32_t n = 0; n < 256; ++n)
			{
				uint32_t c = n;
				for (int k = 0; k < 8; ++k)
					c = (c & 1) ? 0xEDB88320u ^ (c >> 1) : c >> 1;
				table[n] = c;
			}
			ready = true;
		}
		crc = ~crc;
		for (size_t i = 0; i < size; ++i)
			crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
		return ~crc;
	}

	void put_u32(std::vector<uint8_t> &out, uint32_t value)
	{
		out.push_back(static_cast<uint8_t>(value >> 24));
		out.push_back(static_cast<uint8_t>(value >> 16));
		out.push_back(static_cast<uint8_t>(value >> 8));
		out.push_back(static_cast<uint8_t>(value));
	}

	void put_chunk(std::vector<uint8_t> &out, const char type[4], const std::vector<uint8_t> &data)
	{
		put_u32(out, static_cast<uint32_t>(data.size()));
		std::vector<uint8_t> body(type, type + 4);
		body.insert(body.end(), data.begin(), data.end());
		out.insert(out.end(), body.begin(), body.end());
		put_u32(out, crc32_update(0, body.data(), body.size()));
	}

	// rgb: width * height * 3 bytes, top row first.
	std::vector<uint8_t> encode_png(const uint8_t *rgb, uint32_t width, uint32_t height)
	{
		std::vector<uint8_t> raw;
		raw.reserve(static_cast<size_t>(height) * (static_cast<size_t>(width) * 3 + 1));
		for (uint32_t y = 0; y < height; ++y)
		{
			raw.push_back(0); // filter type: none
			raw.insert(raw.end(), rgb + static_cast<size_t>(y) * width * 3, rgb + static_cast<size_t>(y + 1) * width * 3);
		}

		std::vector<uint8_t> z = { 0x78, 0x01 };
		uint32_t a = 1, b = 0;
		for (uint8_t byte : raw)
		{
			a = (a + byte) % 65521;
			b = (b + a) % 65521;
		}
		size_t pos = 0;
		do
		{
			const size_t block = std::min<size_t>(65535, raw.size() - pos);
			const bool last = pos + block >= raw.size();
			z.push_back(last ? 1 : 0);
			z.push_back(static_cast<uint8_t>(block & 0xFF));
			z.push_back(static_cast<uint8_t>(block >> 8));
			z.push_back(static_cast<uint8_t>(~block & 0xFF));
			z.push_back(static_cast<uint8_t>((~block >> 8) & 0xFF));
			z.insert(z.end(), raw.begin() + pos, raw.begin() + pos + block);
			pos += block;
		} while (pos < raw.size());
		put_u32(z, (b << 16) | a);

		std::vector<uint8_t> png = { 0x89, 'P', 'N', 'G', 0x0D, 0x0A, 0x1A, 0x0A };
		std::vector<uint8_t> ihdr;
		put_u32(ihdr, width);
		put_u32(ihdr, height);
		ihdr.insert(ihdr.end(), { 8, 2, 0, 0, 0 }); // 8 bit, RGB, deflate, no filter, no interlace
		put_chunk(png, "IHDR", ihdr);
		put_chunk(png, "IDAT", z);
		put_chunk(png, "IEND", {});
		return png;
	}

	// ---- Commands (run on the render thread) ----

	std::string do_preset(reshade::api::effect_runtime *runtime, const std::string &argument)
	{
		if (argument.empty())
			return "ERR preset needs a path";
		namespace fs = std::filesystem;
		fs::path path = from_utf8(argument);
		const fs::path full = path.is_absolute() ? path : game_folder() / path;
		std::error_code ec;
		if (!fs::is_regular_file(full, ec) || full.extension() != L".ini")
			return "ERR preset file not found: " + argument; // the current preset is left alone
		runtime->set_current_preset_path(argument.c_str());
		return "OK preset=" + argument;
	}

	// The first value each uniform had before this add-on changed it, so "restore" can put it back. Render thread only.
	// ReShade can auto-save a changed uniform into the user's preset, so Soundstage restores on scene removal and on unload.
	struct Original
	{
		reshade::api::format base_type = reshade::api::format::unknown;
		std::vector<float> floats;
		std::vector<int32_t> ints;
		std::vector<uint32_t> uints;
	};
	std::map<std::string, Original> g_originals;

	void remember_original(reshade::api::effect_runtime *runtime, const std::string &name, reshade::api::effect_uniform_variable variable,
		reshade::api::format base_type, size_t count)
	{
		if (g_originals.count(name) != 0)
			return;
		Original original;
		original.base_type = base_type;
		if (base_type == reshade::api::format::r32_sint)
		{
			original.ints.assign(count, 0);
			runtime->get_uniform_value_int(variable, original.ints.data(), count);
		}
		else if (base_type == reshade::api::format::r32_uint)
		{
			original.uints.assign(count, 0);
			runtime->get_uniform_value_uint(variable, original.uints.data(), count);
		}
		else
		{
			original.floats.assign(count, 0.0f);
			runtime->get_uniform_value_float(variable, original.floats.data(), count);
		}
		g_originals[name] = std::move(original);
	}

	size_t restore_originals(reshade::api::effect_runtime *runtime)
	{
		size_t restored = 0;
		for (const auto &entry : g_originals)
		{
			const reshade::api::effect_uniform_variable variable = runtime->find_uniform_variable(nullptr, entry.first.c_str());
			if (variable.handle == 0)
				continue; // the effect was reloaded without it; nothing to put back
			const Original &original = entry.second;
			if (original.base_type == reshade::api::format::r32_sint)
				runtime->set_uniform_value_int(variable, original.ints.data(), original.ints.size());
			else if (original.base_type == reshade::api::format::r32_uint)
				runtime->set_uniform_value_uint(variable, original.uints.data(), original.uints.size());
			else
				runtime->set_uniform_value_float(variable, original.floats.data(), original.floats.size());
			++restored;
		}
		g_originals.clear();
		return restored;
	}

	std::string do_status(reshade::api::effect_runtime *runtime)
	{
		char path[1024] = {};
		size_t size = sizeof(path);
		runtime->get_current_preset_path(path, &size);
		return std::string("OK preset=") + path;
	}

	std::string do_uniform(reshade::api::effect_runtime *runtime, const std::vector<std::string> &parts)
	{
		if (parts.size() < 3 || parts.size() > 6)
			return "ERR usage: uniform <name> <v1> [v2 v3 v4]";
		const reshade::api::effect_uniform_variable variable = runtime->find_uniform_variable(nullptr, parts[1].c_str());
		if (variable.handle == 0)
			return "ERR uniform not found: " + parts[1];

		std::vector<float> values;
		for (size_t i = 2; i < parts.size(); ++i)
		{
			char *end = nullptr;
			const float value = std::strtof(parts[i].c_str(), &end);
			if (end == parts[i].c_str() || *end != '\0')
				return "ERR bad number: " + parts[i];
			values.push_back(value);
		}

		reshade::api::format base_type = reshade::api::format::unknown;
		uint32_t rows = 1, columns = 1;
		runtime->get_uniform_variable_type(variable, &base_type, &rows, &columns);
		if (values.size() > static_cast<size_t>(rows) * columns)
			return "ERR " + parts[1] + " takes " + std::to_string(rows * columns) + " value(s)";

		remember_original(runtime, parts[1], variable, base_type, static_cast<size_t>(rows) * columns);

		if (base_type == reshade::api::format::r32_sint)
		{
			std::vector<int32_t> ints;
			for (float value : values)
				ints.push_back(static_cast<int32_t>(value));
			runtime->set_uniform_value_int(variable, ints.data(), ints.size());
		}
		else if (base_type == reshade::api::format::r32_uint)
		{
			std::vector<uint32_t> uints;
			for (float value : values)
				uints.push_back(value < 0 ? 0u : static_cast<uint32_t>(value));
			runtime->set_uniform_value_uint(variable, uints.data(), uints.size());
		}
		else
		{
			runtime->set_uniform_value_float(variable, values.data(), values.size());
		}

		std::string text = "OK " + parts[1] + "=";
		for (size_t i = 0; i < values.size(); ++i)
			text += (i ? "," : "") + parts[i + 2];
		return text;
	}

	// focus <uniform> <value>: one float, tracked for restore, and not logged per call (auto focus sends several a second).
	std::string do_focus(reshade::api::effect_runtime *runtime, const std::vector<std::string> &parts)
	{
		if (parts.size() != 3)
			return "ERR usage: focus <uniform> <metres>";
		char *end = nullptr;
		const float value = std::strtof(parts[2].c_str(), &end);
		if (end == parts[2].c_str() || *end != '\0' || !std::isfinite(value) || value < 0.0f)
			return "ERR bad number: " + parts[2];
		const reshade::api::effect_uniform_variable variable = runtime->find_uniform_variable(nullptr, parts[1].c_str());
		if (variable.handle == 0)
			return "ERR uniform not found: " + parts[1];
		reshade::api::format base_type = reshade::api::format::unknown;
		uint32_t rows = 1, columns = 1;
		runtime->get_uniform_variable_type(variable, &base_type, &rows, &columns);
		if (base_type != reshade::api::format::r32_float)
			return "ERR " + parts[1] + " is not a float uniform";
		remember_original(runtime, parts[1], variable, base_type, static_cast<size_t>(rows) * columns);
		runtime->set_uniform_value_float(variable, &value, 1);
		return "OK focus " + parts[1] + "=" + parts[2];
	}

	std::string do_uniforms(reshade::api::effect_runtime *runtime)
	{
		std::string names;
		int count = 0;
		runtime->enumerate_uniform_variables(nullptr, [&](reshade::api::effect_runtime *r, reshade::api::effect_uniform_variable variable) {
			if (count >= 200)
				return;
			char name[256] = {};
			size_t size = sizeof(name);
			r->get_uniform_variable_name(variable, name, &size);
			names += (count ? "," : "") + std::string(name);
			++count;
		});
		return "OK " + names;
	}

	std::string do_shot(reshade::api::effect_runtime *runtime, const std::string &argument)
	{
		if (argument.empty())
			return "ERR shot needs a path";
		namespace fs = std::filesystem;
		const fs::path path = from_utf8(argument);
		if (path.extension() != L".png")
			return "ERR shot path must end in .png";

		uint32_t width = 0, height = 0;
		runtime->get_screenshot_width_and_height(&width, &height);
		if (width == 0 || height == 0)
			return "ERR no back buffer yet";

		const reshade::api::resource back_buffer = runtime->get_current_back_buffer();
		const reshade::api::format format = runtime->get_device()->get_resource_desc(back_buffer).texture.format;
		bool bgra = false;
		switch (format)
		{
		case reshade::api::format::r8g8b8a8_typeless:
		case reshade::api::format::r8g8b8a8_unorm:
		case reshade::api::format::r8g8b8a8_unorm_srgb:
			break;
		case reshade::api::format::b8g8r8a8_typeless:
		case reshade::api::format::b8g8r8a8_unorm:
		case reshade::api::format::b8g8r8a8_unorm_srgb:
		case reshade::api::format::b8g8r8x8_typeless:
		case reshade::api::format::b8g8r8x8_unorm:
		case reshade::api::format::b8g8r8x8_unorm_srgb:
			bgra = true;
			break;
		default:
			return "ERR back buffer format " + std::to_string(static_cast<int>(format)) + " not supported";
		}

		std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4);
		if (!runtime->capture_screenshot(pixels.data()))
			return "ERR capture_screenshot failed";

		std::vector<uint8_t> rgb(static_cast<size_t>(width) * height * 3);
		for (size_t i = 0, n = static_cast<size_t>(width) * height; i < n; ++i)
		{
			rgb[i * 3 + 0] = pixels[i * 4 + (bgra ? 2 : 0)];
			rgb[i * 3 + 1] = pixels[i * 4 + 1];
			rgb[i * 3 + 2] = pixels[i * 4 + (bgra ? 0 : 2)];
		}
		const std::vector<uint8_t> png = encode_png(rgb.data(), width, height);

		std::error_code ec;
		if (path.has_parent_path())
			fs::create_directories(path.parent_path(), ec);
		FILE *file = nullptr;
		if (_wfopen_s(&file, path.c_str(), L"wb") != 0 || file == nullptr)
			return "ERR could not open for writing: " + argument;
		const size_t written = fwrite(png.data(), 1, png.size(), file);
		fclose(file);
		if (written != png.size())
			return "ERR short write: " + argument;
		return "OK " + argument + " " + std::to_string(width) + "x" + std::to_string(height);
	}

	std::string run_command(const std::string &line)
	{
		reshade::api::effect_runtime *runtime = g_runtime.load();
		const std::vector<std::string> parts = split(line);
		if (parts.empty())
			return "ERR empty command";
		const std::string &verb = parts[0];

		if (verb == "ping")
			return "OK soundstage-addon " + std::to_string(kProtocolVersion);
		if (verb == "unload")
		{
			// Works with no runtime too: a plugin reload must always be able to free the pipe.
			const size_t restored = runtime != nullptr ? restore_originals(runtime) : 0;
			g_drop_client.store(true);
			return "OK unloaded restored=" + std::to_string(restored);
		}
		if (runtime == nullptr)
			return "ERR no ReShade effect runtime yet";
		if (verb == "status")
			return do_status(runtime);
		if (verb == "preset")
			return do_preset(runtime, rest_of(line));
		if (verb == "uniform")
			return do_uniform(runtime, parts);
		if (verb == "focus")
			return do_focus(runtime, parts);
		if (verb == "restore")
			return "OK restored=" + std::to_string(restore_originals(runtime));
		if (verb == "uniforms")
			return do_uniforms(runtime);
		if (verb == "shot")
			return do_shot(runtime, rest_of(line));
		return "ERR unknown command: " + verb;
	}

	void on_present(reshade::api::effect_runtime *)
	{
		std::deque<std::shared_ptr<Job>> jobs;
		{
			std::lock_guard<std::mutex> lock(g_queue_mutex);
			jobs.swap(g_queue);
		}
		for (const std::shared_ptr<Job> &job : jobs)
		{
			{
				std::lock_guard<std::mutex> lock(job->mutex);
				if (job->abandoned)
					continue;
			}
			std::string reply;
			try { reply = run_command(job->line); }
			catch (const std::exception &ex) { reply = std::string("ERR exception: ") + ex.what(); }
			catch (...) { reply = "ERR unknown exception"; }
			if (job->line.rfind("focus ", 0) != 0 || reply.rfind("OK", 0) != 0)
				log(reply.rfind("OK", 0) == 0 ? reshade::log::level::info : reshade::log::level::warning, job->line + " -> " + reply);
			{
				std::lock_guard<std::mutex> lock(job->mutex);
				job->reply = reply;
				job->done = true;
			}
			job->cv.notify_all();
		}
	}

	void on_init_runtime(reshade::api::effect_runtime *runtime)
	{
		g_runtime.store(runtime);
		log(reshade::log::level::info, "effect runtime ready, add-on protocol " + std::to_string(kProtocolVersion) + ", pipe \\\\.\\pipe\\soundstage-reshade");
	}

	void on_destroy_runtime(reshade::api::effect_runtime *runtime)
	{
		reshade::api::effect_runtime *expected = runtime;
		if (g_runtime.compare_exchange_strong(expected, nullptr))
			g_originals.clear(); // the effects go with the runtime; their uniforms no longer exist
	}

	std::string dispatch(const std::string &line)
	{
		// ping works without the render thread, so the plugin can tell "add-on not loaded" from "game not rendering".
		if (line.rfind("ping", 0) == 0)
			return run_command(line);
		if (line.rfind("unload", 0) == 0 && g_runtime.load() == nullptr)
			return run_command(line);

		const auto job = std::make_shared<Job>();
		job->line = line;
		{
			std::lock_guard<std::mutex> lock(g_queue_mutex);
			g_queue.push_back(job);
		}
		std::unique_lock<std::mutex> lock(job->mutex);
		if (!job->cv.wait_for(lock, kFrameWait, [&] { return job->done; }))
		{
			job->abandoned = true;
			return "ERR timed out waiting for a frame";
		}
		return job->reply;
	}

	DWORD WINAPI pipe_thread(LPVOID)
	{
		while (!g_stop.load())
		{
			const HANDLE pipe = CreateNamedPipeW(kPipeName, PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
				1, 65536, 65536, 0, nullptr);
			if (pipe == INVALID_HANDLE_VALUE)
			{
				Sleep(1000);
				continue;
			}
			if (ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED)
			{
				std::string buffer;
				char chunk[1024];
				DWORD got = 0;
				while (!g_stop.load() && ReadFile(pipe, chunk, sizeof(chunk), &got, nullptr) && got > 0)
				{
					buffer.append(chunk, got);
					size_t newline;
					while ((newline = buffer.find('\n')) != std::string::npos)
					{
						const std::string line = buffer.substr(0, newline);
						buffer.erase(0, newline + 1);
						const std::string reply = dispatch(line) + "\n";
						DWORD sent = 0;
						WriteFile(pipe, reply.data(), static_cast<DWORD>(reply.size()), &sent, nullptr);
						if (g_drop_client.exchange(false))
						{
							buffer.clear();
							goto client_done; // "unload": let go of this client now
						}
					}
				}
			client_done:
				FlushFileBuffers(pipe);
				DisconnectNamedPipe(pipe);
			}
			CloseHandle(pipe);
		}
		return 0;
	}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
	switch (reason)
	{
	case DLL_PROCESS_ATTACH:
		if (!reshade::register_addon(module))
			return FALSE;
		reshade::register_event<reshade::addon_event::init_effect_runtime>(on_init_runtime);
		reshade::register_event<reshade::addon_event::destroy_effect_runtime>(on_destroy_runtime);
		reshade::register_event<reshade::addon_event::reshade_present>(on_present);
		reshade::log::message(reshade::log::level::info, "[Soundstage] add-on loaded");
		g_pipe_thread = CreateThread(nullptr, 0, pipe_thread, nullptr, 0, nullptr);
		break;
	case DLL_PROCESS_DETACH:
		g_stop.store(true);
		if (g_pipe_thread != nullptr)
		{
			CancelSynchronousIo(g_pipe_thread);
			CloseHandle(g_pipe_thread);
		}
		reshade::unregister_addon(module);
		break;
	}
	return TRUE;
}
