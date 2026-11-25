# Troubleshooting

Start with the **Status** tab in the Soundstage window. It asks Stagehand, Brio, the ReShade add-on and your AI key whether
they are answering right now, and says what to do about each one that is not. Most problems end there.

If it doesn't, the log has more. Every Soundstage line in `dalamud.log` starts with a tag in square brackets, so searching
for `Soundstage/` finds them all. The tags: `SH` (Stagehand), `BRIO`, `AI`, `CAM` (camera), `ANIM` (animated lights),
`SET`, `BODY`, `TEST` (the checklist), `MIGRATE`, `STATUS`. A key is never in any of them.

Rows below are grouped by area. Each is a symptom, what is probably going on, and what to do.

## Install and the Status tab

| Symptom | Cause | Fix |
|---|---|---|
| Stagehand row is red | Stagehand isn't installed, or it's switched off. The two look the same from outside, because Soundstage asks it over IPC. | Install Stagehand 0.5.x from the plugin installer, make sure it's enabled, press **Recheck**. Lights, Sets and Scenes need it. |
| Stagehand row is red but it is installed and on | Stagehand moved to an API this build doesn't speak. | Update Soundstage and Stagehand, then **Recheck**. The Status row names what it saw. |
| Brio row is red | Brio isn't installed, it's off, or its IPC is off. | Install Brio 0.8.x, switch IPC on in Brio's settings, **Recheck**. The Poses shelf and the pose tools need it. |
| Brio row says another API version | Brio and Soundstage disagree on the IPC version. | Update both from the plugin installer. |
| Lighting tab is greyed out | Stagehand isn't answering (see above). The tab says so in red at the top. | Fix Stagehand, then **Recheck** on the Status tab. |
| ReShade add-on row is amber | The add-on isn't in the game folder, or the game hasn't been restarted since you copied it in. It's optional. | Copy `soundstage.addon64` next to `dxgi.dll` (**Open the game folder** gets you there) and restart the game. Soundstage never writes into the game folder itself. |
| Add-on row says it is an older version | A newer plugin than add-on. | Close the game, copy the newer `soundstage.addon64` over the old one, start the game. |
| AI key row is amber | No key, or a key that hasn't been tested yet this session. | Press **Test key** (it only lists models, so no quota and no picture goes anywhere). SETUP-AI.md has the steps. |
| Status says everything's fine but a tab does nothing | You're not in GPose. Camera, Poses, Look and the capture buttons only work there. | Enter GPose. |

## Lights, sets and scenes

| Symptom | Cause | Fix |
|---|---|---|
| Lights vanish when you change zone | Stagehand drops temporary stages on a location change. | Soundstage re-sends what you had placed and keeps trying for about 10 seconds. If it gives up, press **Apply** again. |
| A Scene has fewer lights than it should | The plugin caps a Scene at six lights until the six-light test in the checklist says more is safe. | Run the six-light test from the Tools tab (`/soundstage dev`). A user Scene with extra lights is cut to six and the log says so. |
| Light markers show in a screenshot | Markers are a dot per light drawn on screen. | They hide with the game UI (Scroll Lock). **Show light markers** turns them off for good. |
| The backdrop sweep has a visible join | Pieces of the dyeable partition may show a seam under the game's lighting. This hasn't been measured. | Try a narrower width, or a darker colour, or use the single **Panel** backdrop type. Tell me which Scene it was. |
| Furniture in a kit faces the wrong way | Models' fronts are inferred from their shape. Some kits turn some pieces 180 degrees. | Tick **Flip 180** on the kit. |
| An atmosphere item (haze, dust, music) does nothing | Each one is marked `untested` until it's been seen in game, and some game effects only show in some zones. | Try another item. Items that don't work are dropped from `atmosphere.json` as they're found. |
| A Scene with strobing light worries you | A strobe is capped at 3 flashes a second and ships switched off. The Club strobe Scene carries a photosensitivity note. | Leave the strobe switched off, or don't use that Scene. |
| Exposure colours (amber, red) seem too jumpy or too relaxed | The two thresholds are starting guesses. | Move them on the Lighting tab's exposure settings. They save. |
| A share code is refused | Codes are one Scene, up to 8,192 characters, and they refuse anything shaped like a key. | Share one Scene at a time. If it was refused for looking like a key, take that text out of the Scene's description. |

## Camera

| Symptom | Cause | Fix |
|---|---|---|
| A framing preset lands on the wrong side of the character, or looks up from below | Soundstage guesses which way the game counts yaw and pitch, and those guesses can be flipped. | On the Camera tab, open **If the camera lands on the wrong side** and set **Yaw trim** to 180 (that puts it on the other side) or tick **Flip the tilt**. It takes effect at once, no restart. |
| The camera's position change snaps back | The game resets camera position. Soundstage writes distance, yaw and pitch, which stick. | Use the distance, yaw and pitch controls instead of a position nudge. |
| The lens (FOV) value does nothing | The GPose FOV is written to a raw offset that may move after a game patch. | The Camera checklist step for FOV says whether it works. A patch that moves it needs a one-number fix in the plugin. |
| Camera tab is greyed out with a reason | The game refused a camera write, or you aren't in GPose. Nothing was changed. | Enter GPose. If it still refuses, the reason on the tab is also in the log under `Soundstage/CAM`. |
| Turntable stopped by itself | It stops when you leave GPose, press Stop, or a write is refused. It then puts the camera back. | Start it again in GPose. |
| Thirds or golden-ratio overlay is missing | Overlays are off by default, and hide with the game UI. | Switch the one you want on in the Camera tab. |

## Look (ReShade)

| Symptom | Cause | Fix |
|---|---|---|
| Look tab says there's no add-on | The add-on isn't loaded (see the Status tab). Without it, the tab gives you a preset name to copy and switch by hand. | Copy the add-on in and restart the game. |
| Your ReShade preset file changed after using Look | If `AutoSavePreset=1` is set in `ReShade.ini`, ReShade can save a value the add-on pushed (like the focus distance) into your preset. The add-on remembers the first value of every uniform it writes and puts it back when you remove the Scene or the plugin unloads. | Keep a copy of your preset, or turn `AutoSavePreset` off in ReShade's settings. The Look tab warns when it's on. **Put it back** switches to the preset you had before. |
| Auto focus pulls to the wrong depth | The distance comes from the game camera and your scale and offset on the Look tab. | Adjust the offset. If the focus uniform has a different range in your shader, change the scale. |
| Screenshots come out without the ReShade look | A capture route that doesn't include ReShade was used. | On the Workflow tab, put the add-on route first, or the key route if you've bound one. |

## Poses and pose tags

| Symptom | Cause | Fix |
|---|---|---|
| The shelf is empty | No pose folders found, or the scan hasn't run. | Add the folder under **Pose folders** and press **Rescan**. The scan stops at 5,000 files and says so. |
| A pose loads body or expression only | The **Body** and **Expression** boxes. Brio has no body-only option, so Soundstage strips the other half out first. | Tick both for a full load. |
| Brio's undo list is full of pose loads | Hover preview loads a pose every time you rest the mouse on a row, and every load is an undo step in Brio. | Turn **Hover preview** off, or click rows instead. |
| The pose didn't go back after hovering | Hover preview ends when you leave the list, switch tab, close the window or leave GPose, and puts back the pose it read first. If you click, the hovered pose stays. | Press Brio's undo if it ever sticks. |
| Make thumbnails stops | It needs GPose, the window out of the way, and the booth and contact sheet idle. | Frame your character first. If a pose won't load it's skipped and counted; a failed capture stops the job. |
| Tag with AI paused | The day's free allowance is used up (or the provider said slow down). Nothing is sent without a button press. | Press **Continue tagging** tomorrow. The count is shared with the Photo tab. |
| The consent prompt came back | The privacy note changed (version 2 covers photos and pose thumbnails), so an older yes is asked again. | Read it and say yes if you're happy. |

## Photo, Face, Expression and Body

| Symptom | Cause | Fix |
|---|---|---|
| Photo tab won't take a file | The intake refuses anything it can't read. See the list below. | Use a JPEG, PNG, BMP, GIF or TGA under the size limit. |
| The Scene from a photo has the key on the wrong side | Left and right are the photo's. The table that flips them is a guess. | Use **Adjust** on the Lighting tab, and tell me which photo. |
| Face tab says the helper isn't installed | The face helper is a separate download, fetched only when you press install. It's about 122 MB to download and 327 MB unpacked, plus the pose model at 9 MB if you use the body tools. | Press **Download the face helper** on the Face tab. It checks every file against a pinned hash and keeps it in the plugin's own folder. **Remove face helper** on the same tab deletes it. |
| The helper download fails or is slow | It comes from python.org, PyPI and Google, and the plugin only accepts those three hosts. | Check your connection and try again. A refused file is deleted and nothing half-installed is used. |
| Antivirus flags the helper | It's a real Python and a pile of wheels, not a single bundled exe, which is the less flaggable shape, but some tools still look twice. | Allow the plugin's `face-helper` folder, or skip the face tools. Nothing else in the plugin needs it. |
| The light estimate from a face is off | The estimate uses guessed cheek and forehead angles until they're calibrated. The head-angle reading itself was checked for consistency, not for absolute accuracy, and may carry a pitch offset. | Use **Pitch bias** on the Face tab. Treat the light as a starting point. |
| Expression match does nothing | A run only advances while the Expression tab is the selected tab, because each picture hides the window and the tab drives the loop. The face helper also has to be installed. | Stay on the tab until it finishes. |
| Expression results look unconvincing | Step sizes and ranges are starting values. The tab is labelled experimental when its own ratings come in low. | Calibrate again after changing the Tuning sliders. A change makes the old table stale. |
| Body match refuses to start | Before moving anything it checks that the character's current pose looks like a standing body. If most segments disagree the bone map is probably wrong for this skeleton. | Reset to a plain standing pose first. There's a setting to apply anyway if you know better. |
| Body match comes out mirrored | The model's left and right axes may not match the game's. | Tick **Mirror** on the body section of the Expression tab. |

## The AI layer

Every message on the AI tab comes from one list in the plugin. This table is generated from it, so it matches.

<!-- ai-rows:start (generated by tools/gen_troubleshooting.py, do not edit) -->
| What you see on the AI tab | Why |
|---|---|
| The provider did not accept that key. Check you copied the whole key with no spaces, or make a fresh one with Get a key. | The service said the key is wrong, expired, revoked or lacks access (401, 403, or 400 "key not valid"). |
| The provider says you have used up its free allowance for now (too many requests, or today's quota). Wait a minute and try again, or come back tomorrow. | 429 or the provider's quota-exceeded status: too fast, or today's free allowance is used up. |
| The provider is not available where you are (it refused the location). Try another provider from the list. | The provider does not serve this country or region (its own location error). |
| The provider does not know that model name, or your key cannot use it. Press Test key to pick one it can see, or fix the name in the Model box. | 404 or a model-not-found body: the model name is wrong or the key cannot see it. |
| Could not reach the internet. Check your connection and try again. | No connection: DNS, refused, offline, TLS. |
| The provider took too long to answer. Try again in a moment. | The request ran out of time. |
| The provider replied, but not in a form Soundstage can read. Try again, or try a different model. | The reply was not JSON we could read. |
| The provider replied, but left out something Soundstage needs. Try again, or try a different model. | The reply was JSON but did not match the schema we asked for. |
| The provider is having trouble on its end. Try again in a few minutes. | 5xx from the provider. |
| The provider refused the request (the model may not accept images, or the picture was blocked). Try a different model or picture. | The provider refused the request itself (400 other than the cases above, or a safety block). |
| There is no key saved for the provider yet. Paste one on the AI tab (Get a key opens the page where you make it). | No key has been saved for the chosen provider. |
| Nothing was sent. You need to say yes to the privacy note on the AI tab before a picture can be uploaded. | The one-time consent for this provider has not been given, so nothing was sent. |
| The settings for the provider are not complete. Check the web address and model on the AI tab. | The settings are incomplete (no model, bad web address). |
| Something unexpected went wrong talking to the provider. Try again, and check dalamud.log for a line starting [Soundstage/AI]. | Anything else. |
<!-- ai-rows:end -->

Photo intake refusals, from the same place:

<!-- intake-rows:start (generated by tools/gen_troubleshooting.py, do not edit) -->
| What you see on the Photo tab | Class |
|---|---|
| Pick a photo first. | NoPath |
| That file was not found. | NotFound |
| That file is N MB. The limit is N MB. | TooBig |
| That file could not be opened. | Unreadable |
| That file is too big. | TooBig |
| That is not a picture Soundstage can read. Use a JPEG, PNG, BMP, GIF or TGA file. | Unsupported |
| That picture is bigger than 40 megapixels. Save a smaller copy and try again. | TooManyPixels |
| That picture could not be read. | Unreadable |
<!-- intake-rows:end -->

| Symptom | Cause | Fix |
|---|---|---|
| Test key works but the test picture fails with a Gemini message | Google has changed its image endpoint before. `docs/ai-providers.md` lists the first things to check (the endpoint, the revision header, the model). | Pick another model in the Model box, or try OpenRouter. |
| The key is gone after a Windows update or on another PC | The key is encrypted for your Windows account on that PC. Another account or another PC reads it as no key. | Paste it again. |
| Worried the key is somewhere it shouldn't be | It's in one request header, and in your settings as encrypted text. It's never in a log line, a Scene, a share code or a pose tag, and the offline checks feed a made-up key through hostile replies to prove it. | Nothing to do. **Forget key** on the AI tab deletes it. |

## Capture and workflow

| Symptom | Cause | Fix |
|---|---|---|
| Capture does nothing | The routes are tried in order and each has a timeout. All of them failed or timed out. | Check the Workflow tab's route order. The key route watches ReShade's save folder if it exists, otherwise the game's screenshot folder; check it's the right one. |
| The booth or contact sheet leaves the window hidden | It restores everything on every way out, even on an error. | Press **Stop**. If anything is still hidden, `/soundstage` brings the window back. |
| The contact sheet changed my Scene | It applies each Scene to photograph it, then puts yours back. The Scenes it tried show up on the undo list. | Use undo on the Scenes tab. |
| The UI doesn't hide for a capture | Hiding the game UI sends Scroll Lock, and waits up to 1.5 seconds for the UI to go. | Untick **Hide the game UI for a capture** on the Workflow tab and hide it yourself. |

## Still stuck

Open the Tools tab (`/soundstage dev`) and run **Test checklist**. It walks through each feature, says what a pass looks like,
and writes the result to `dalamud.log`. Copy the summary and send it along with the Soundstage lines from the log.
