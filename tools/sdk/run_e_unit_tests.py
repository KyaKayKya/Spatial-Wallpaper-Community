"""Run SDK tests in an isolated process for the E case catalog."""
from pathlib import Path
import subprocess
import sys

if __name__ == '__main__':
    root = Path(__file__).resolve().parents[2]
    raise SystemExit(subprocess.call([sys.executable, '-m', 'unittest', 'discover',
                                     '-s', 'tools/sdk/tests', '-v'], cwd=root))
