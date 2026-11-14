"""One bounded readiness line from an owned child; caller always owns termination."""
import queue
import threading

def ready_line(process, timeout):
    result = queue.Queue(maxsize=1)
    def read():
        try:
            result.put((True, process.stdout.readline(1025)))
        except Exception as error:
            result.put((False, error))
    thread = threading.Thread(target=read, daemon=True, name='sdk-ready-line')
    thread.start()
    try:
        success, value = result.get(timeout=timeout)
    except queue.Empty as error:
        raise TimeoutError('child-readiness-deadline') from error
    thread.join(timeout=0.1)
    if not success:
        raise value
    if not value or len(value)>1024 or not value.endswith('\n'):
        raise ValueError('child-readiness-line-shape')
    return value
