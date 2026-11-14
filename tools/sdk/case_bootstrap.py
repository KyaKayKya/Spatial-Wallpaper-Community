"""Trusted gate: do not load target code or site hooks until Job assignment."""
import sys


def main():
    if sys.stdin.buffer.readline(16) != b'RUN\n':
        return 125
    # Python starts with -I -S. Site hooks and target imports can only execute
    # after the parent's private pipe release, while this process is in its Job.
    import site
    site.main()
    import runpy
    from pathlib import Path
    target = str(Path(sys.argv[1]).resolve())
    sys.argv = [target, *sys.argv[2:]]
    sys.path.insert(0, str(Path(target).parent))
    runpy.run_path(target, run_name='__main__')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
