"""Local camera diagnostic: reports modes and error codes, never saves frames."""
import asyncio
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tracker/src'))
from spatial_tracker.camera import WindowsCamera

async def main():
    camera = WindowsCamera()
    count = 0
    def frame(rgb, timestamp):
        nonlocal count
        count += 1
    try:
        print(await camera.open(sys.argv[1], frame, lambda e: print(type(e).__name__, str(e))), flush=True)
        await asyncio.sleep(2)
        print('frames=', count)
    except Exception as exc:
        import traceback
        traceback.print_exc()
        print(type(exc).__name__, str(exc), flush=True)
    finally:
        await camera.close()

asyncio.run(main())
