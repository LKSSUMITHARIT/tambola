"""Generates placeholder Hindi caller clips (wwwroot/audio/hi/{n}.mp3) from calls.json using edge-tts.
Existing files are skipped, so real recordings dropped in with the same name are never overwritten.
Usage: python tools/gen_voice.py [--voice hi-IN-MadhurNeural] [--rate +10%] [--pitch +8Hz] [--force]"""
import argparse, asyncio, json, pathlib
import edge_tts

DIR = pathlib.Path(__file__).resolve().parent.parent / "src/Tambola.Web/wwwroot/audio/hi"

async def one(sem, n, text, a):
    f = DIR / f"{n}.mp3"
    if f.exists() and not a.force: return
    async with sem:
        for attempt in range(3):
            try:
                await edge_tts.Communicate(text, a.voice, rate=a.rate, pitch=a.pitch).save(str(f)); print("ok", n); return
            except Exception as e:
                print("retry", n, e); await asyncio.sleep(1 + attempt)
        print("FAILED", n)

async def main():
    p = argparse.ArgumentParser()
    p.add_argument("--voice", default="hi-IN-MadhurNeural"); p.add_argument("--rate", default="+10%")
    p.add_argument("--pitch", default="+8Hz"); p.add_argument("--force", action="store_true")
    a = p.parse_args()
    calls = json.loads((DIR / "calls.json").read_text(encoding="utf-8"))
    sem = asyncio.Semaphore(4)
    await asyncio.gather(*(one(sem, n, t, a) for n, t in calls.items()))

asyncio.run(main())
