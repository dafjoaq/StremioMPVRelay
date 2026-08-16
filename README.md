# stremioMPVRelay

basically a small Windows app i made that sends Stremio Torrentio addon streams to MPV so i don't have to keep reopening videos in my favorite external player (i use MPV).

i made this for C# practice and for myself, so i imagine it's not really that usable for others. tho i'll still get this out so im sure someones bound to make a better version or tell me off for a better way lolol i'll still update mine tho and i'll improve it as i learn

## how to use

1. paste your Stremio / Torrentio addon manifest URL (**the configure Torrentio page `torrentio.strem.fun`; click `Copy Link` right under install**)
2. select your `mpv.exe`
3. enter the IMDb ID for the show you want to watch  
   example: `https://www.imdb.com/title/<ID>/`
4. the app should autodetect the title and episode info
5. pick whatever stream filters you want
6. click **Connect MPV**
7. click **Start**

that's basically it

the app grabs streams from the addon, picks one based on your settings, sends it to MPV, and keeps the next episodes queued so playback can keep going

## stuff it can do

- autodetect title and episode info from an IMDb ID
- choose quality / provider / minimum seeders
- rank and filter available streams
- queue upcoming episodes in MPV
- keep simple watch history
- remember episode progress for resume
- retry another stream if one fails
- a few extra MPV speed controls through the included Lua script; i use this myself

## requirements

- Windows
- .NET 8 Desktop Runtime
- MPV
- a Stremio-compatible addon manifest URL
