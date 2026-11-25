# Setting up the AI bits

The AI layer is optional. Soundstage lights, sets, poses, camera and look all work with no key at all. A key switches on
three things, and only when you press their buttons:

- **Photo tab:** pick a photo, and it suggests a Scene (light direction, softness, colours) you can apply, adjust or save.
- **Poses tab, Tag with AI:** reads a small picture of your character in each pose and writes tags so the shelf can filter by them.
- Anything later that uses the same door.

You bring your own key. Nothing goes anywhere until you've pasted one in and said yes to the privacy note.

## The three providers

| Provider | Free? | Good for |
|---|---|---|
| Google Gemini | Has a free tier | The easiest start. Reads pictures well. |
| OpenRouter | Has free models (names ending `:free`) with a small daily allowance | A free fallback if Gemini isn't available where you are. |
| Your own endpoint | Depends on what you point it at | Anything that speaks the OpenAI chat format, including a model running on your own PC. You type the web address (ending in `/v1`) and a model that takes images. |

## Getting a free Gemini key

1. Open the AI tab in Soundstage, pick **Google Gemini**, and press **Get a key**. It opens Google AI Studio's key page
   (`aistudio.google.com/apikey`).
2. Sign in with a Google account and make a key.
3. Copy it and paste it into the **Key** box on the AI tab. Press **Save key**.
4. Press **Test key** (more on that below).

## Getting an OpenRouter key

1. On the AI tab pick **OpenRouter** and press **Get a key**. It opens `openrouter.ai/keys`.
2. Make an account and a key, copy it, paste it into the **Key** box and press **Save key**.
3. Press **Test key**. If the Model box is empty it suggests the first free model that accepts pictures, and shows how many
   free requests you have left today.

## Test key

**Test key** asks the provider for its list of models using your key. That's all it does:

- It sends no picture, so it doesn't ask for the privacy note.
- It costs no quota.
- If it works, you see a green message. If not, you see a plain sentence about what went wrong (wrong key, not available
  in your region, no internet, and so on). Those sentences are all in `docs/troubleshooting.md`.

The Status tab shows the same thing as a row: amber until the key has been tested this session, green once it passes, red with
the message if it fails. It never tests the key on its own. You press the button.

**Send a test picture** sends a 32 by 32 orange and blue square, one request, to prove the whole path (the privacy note, the
upload, reading the reply). It asks for the privacy note first.

## The privacy note (consent)

The first time a picture would leave your PC, Soundstage shows a note and asks. It says where the picture goes, and what the
provider may do with it, in the provider's own terms. For Gemini's free tier that includes Google using what you send to improve its
products. For OpenRouter it depends on the model you pick, so it tells you to check the model's page.

- It covers photos you pick on the Photo tab **and** the small pose pictures Tag with AI sends from the Poses tab. It's one
  yes per provider (per web address for your own endpoint).
- The note has a version number. This release is **consent version 2** because it added the pose pictures, so a yes from an
  earlier build gets asked again, once.
- **No, send nothing** closes it and nothing is sent. **Withdraw my yes** on the AI tab takes a yes back.

## Where the key lives, and where it doesn't

- On your PC only, in Soundstage's settings file, encrypted for your Windows account (Windows DPAPI). Another Windows user, or
  the same files copied to another PC, can't read it and will show "no key". Paste it again there.
- Sent in one place: a request header to the provider, over https (or plain http to a server on your own PC).
- **Never** in a URL, a log line, an error message, a Scene, a share code or a pose tag. The offline checks push a made-up key
  through a pile of hostile replies from fake servers on all three providers and fail if it turns up anywhere. A share code
  is refused if it contains your key or something shaped like one.
- **Forget key** on the AI tab deletes it.

## Limits

Free allowances change. These are what the providers' own pages said when this was written, on 21/11/2026. Check the page
before you rely on a number (the AI tab has a **Check the provider's limits** button).

| Provider | What the page said (21/11/2026) |
|---|---|
| Google Gemini | Limits apply per project and reset at midnight Pacific time. The page's table named models that don't match the ones Soundstage picks, so no figure is quoted here. See `aistudio.google.com/rate-limit` for yours. Soundstage shows how many requests it has sent today. |
| OpenRouter (`:free` models) | 20 requests a minute. 50 a day if you've bought under 10 credits, 1,000 a day if you've bought at least 10. |
| Your own endpoint | Whatever you've set up. |

Tag with AI spaces its requests out, pauses itself when the day's allowance is used up, and carries on when you press
**Continue tagging**. The count of requests sent today is shared with the Photo tab.

## Something not working?

`docs/troubleshooting.md` has every message the AI tab can show, with what it means. `docs/ai-providers.md` records what each
provider's documentation said and what to check first if an endpoint changes.
