"""Fixed command launcher, executed only after its bootstrap enters the Job."""
import subprocess
import sys

if __name__ == '__main__':
    result = subprocess.run(sys.argv[1:], check=False)
    sys.exit(result.returncode)
