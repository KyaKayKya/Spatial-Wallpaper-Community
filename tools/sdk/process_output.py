"""Read the final nonempty output line, tolerating BOMs from Windows managed runtimes."""
def artifact_line(output):
 lines=[line.strip().strip('\ufeff').strip() for line in output.splitlines()]
 return next(line for line in reversed(lines) if line)
