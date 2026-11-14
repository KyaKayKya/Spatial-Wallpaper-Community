"""Parent-owned Windows Job containing a gated Python case and its descendants."""
import ctypes as c
import hashlib
from ctypes import wintypes as w
import os
from pathlib import Path
import subprocess
import sys
import time


def toolchain_helpers():
    # Same installation/version selection as experiments/native_build.py.
    # Exact paths and bytes are captured before the target is released.
    root = Path('C:/Program Files/Microsoft Visual Studio/2022/Community/VC/Tools/MSVC')
    try:
        vc = sorted(root.iterdir())[-1]
        helper = (vc/'bin/Hostx64/x64/vctip.exe').resolve()
        return {str(helper).casefold(): hashlib.sha256(helper.read_bytes()).hexdigest()}
    except (OSError, IndexError):
        return {}


class Basic(c.Structure):
    _fields_ = [('process_time', c.c_longlong), ('job_time', c.c_longlong),
                ('flags', w.DWORD), ('minimum', c.c_size_t), ('maximum', c.c_size_t),
                ('active', w.DWORD), ('affinity', c.c_size_t),
                ('priority', w.DWORD), ('scheduling', w.DWORD)]


class Io(c.Structure):
    _fields_ = [('values', c.c_ulonglong * 6)]


class Limits(c.Structure):
    _fields_ = [('basic', Basic), ('io', Io), ('process_memory', c.c_size_t),
                ('job_memory', c.c_size_t), ('peak_process', c.c_size_t), ('peak_job', c.c_size_t)]


class Accounting(c.Structure):
    _fields_ = [('times', c.c_longlong * 4), ('faults', w.DWORD),
                ('total', w.DWORD), ('active', w.DWORD), ('terminated', w.DWORD)]


class Job:
    def __init__(self):
        if os.name != 'nt':
            raise RuntimeError('Windows case Job required; no uncontained fallback')
        self.k = c.WinDLL('kernel32', use_last_error=True)
        self.member_handles = []
        k = self.k
        k.CreateJobObjectW.argtypes = [c.c_void_p, w.LPCWSTR]
        k.CreateJobObjectW.restype = w.HANDLE
        k.SetInformationJobObject.argtypes = [w.HANDLE, c.c_int, c.c_void_p, w.DWORD]
        k.SetInformationJobObject.restype = w.BOOL
        k.AssignProcessToJobObject.argtypes = [w.HANDLE, w.HANDLE]
        k.AssignProcessToJobObject.restype = w.BOOL
        k.TerminateJobObject.argtypes = [w.HANDLE, w.UINT]
        k.TerminateJobObject.restype = w.BOOL
        k.QueryInformationJobObject.argtypes = [w.HANDLE, c.c_int, c.c_void_p, w.DWORD, c.c_void_p]
        k.QueryInformationJobObject.restype = w.BOOL
        k.CloseHandle.argtypes = [w.HANDLE]
        k.CloseHandle.restype = w.BOOL
        k.OpenProcess.argtypes = [w.DWORD,w.BOOL,w.DWORD]
        k.OpenProcess.restype = w.HANDLE
        k.IsProcessInJob.argtypes = [w.HANDLE,w.HANDLE,c.POINTER(w.BOOL)]
        k.IsProcessInJob.restype = w.BOOL
        k.QueryFullProcessImageNameW.argtypes = [w.HANDLE,w.DWORD,w.LPWSTR,c.POINTER(w.DWORD)]
        k.QueryFullProcessImageNameW.restype = w.BOOL
        k.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
        k.WaitForSingleObject.restype = w.DWORD
        # NULL security attributes: the Job handle is not inherited by children.
        self.handle = k.CreateJobObjectW(None, None)
        if not self.handle:
            raise c.WinError(c.get_last_error())
        limits = Limits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, no breakaway.
        if not k.SetInformationJobObject(self.handle, 9, c.byref(limits), c.sizeof(limits)):
            error = c.WinError(c.get_last_error())
            self.close()
            raise error

    def assign(self, process):
        if not self.k.AssignProcessToJobObject(self.handle, int(process._handle)):
            raise c.WinError(c.get_last_error())

    def terminate(self):
        if not self.k.TerminateJobObject(self.handle, 1):
            raise c.WinError(c.get_last_error())

    def active(self):
        accounting = Accounting()
        if not self.k.QueryInformationJobObject(self.handle, 1, c.byref(accounting), c.sizeof(accounting), None):
            raise c.WinError(c.get_last_error())
        return accounting.active

    def close(self):
        for process in self.member_handles:
            self.k.CloseHandle(process)
        self.member_handles.clear()
        if self.handle:
            if not self.k.CloseHandle(self.handle):
                raise c.WinError(c.get_last_error())
            self.handle = None

    def members(self):
        # This lists only this retained Job, never the machine's process table.
        class Ids(c.Structure):
            _fields_=[('assigned',w.DWORD),('count',w.DWORD),('ids',c.c_size_t*1024)]
        ids=Ids()
        if not self.k.QueryInformationJobObject(self.handle,3,c.byref(ids),c.sizeof(ids),None):
            return {'unmeasured':repr(c.WinError(c.get_last_error()))}
        rows=[]
        for pid in list(ids.ids)[:min(ids.count,1024)]:
            row={'pid':pid};process=self.k.OpenProcess(0x101000,False,pid)
            if process:
                try:
                    belongs=w.BOOL()
                    if self.k.IsProcessInJob(process,self.handle,c.byref(belongs)) and belongs.value:
                        name=c.create_unicode_buffer(32768);size=w.DWORD(len(name))
                        if self.k.QueryFullProcessImageNameW(process,0,name,c.byref(size)):
                            row['image']=name.value
                        self.member_handles.append(process)
                        process = None
                finally:
                    if process:
                        self.k.CloseHandle(process)
            rows.append(row)
        return rows

    def wait_members(self):
        deadline = time.monotonic() + 5
        for process in self.member_handles:
            remaining = max(0, int((deadline - time.monotonic()) * 1000))
            if self.k.WaitForSingleObject(process, remaining) != 0:
                raise TimeoutError('case-member-kernel-exit-unproven')


class CaseProcess:
    def __init__(self):
        self.job = Job()
        self.process = None
        self.closed = False
        self.helpers = toolchain_helpers()
        self.evidence = {'job_kill_on_close': True, 'assignment_complete': False,
                         'handshake_released': False, 'active_after_termination': None,
                         'root_kernel_exited': False, 'allowed_toolchain_helpers': self.helpers}

    def start(self, script, arguments, cwd, stdout, env=None):
        if self.process is not None or self.closed:
            raise RuntimeError('case process cannot be restarted')
        bootstrap = Path(__file__).with_name('case_bootstrap.py')
        command = [sys.executable, '-I', '-S', str(bootstrap), str(script), *arguments]
        self.evidence['launch_command'] = command
        self.process = subprocess.Popen(
            command,
            cwd=cwd, env=env, stdin=subprocess.PIPE, stdout=stdout, stderr=subprocess.STDOUT,
            creationflags=subprocess.CREATE_NO_WINDOW, close_fds=True)
        self.evidence['root_pid'] = self.process.pid
        # This is the only pre-assignment process: the fixed bootstrap cannot
        # import target/site code or spawn descendants before its private release.
        self.job.assign(self.process)
        self.evidence['assignment_complete'] = True
        self.release()

    def release(self):
        self.process.stdin.write(b'RUN\n')
        self.process.stdin.flush()
        self.evidence['handshake_released'] = True
        self.process.stdin.close()

    def wait(self, timeout):
        return self.process.wait(timeout=timeout)

    def close(self):
        if self.closed:
            return
        errors = []
        cleanup_started = time.monotonic()
        self.evidence['termination_reason'] = 'runner-exited' if self.process and self.process.poll() is not None else 'runner-incomplete-or-startup-failure'
        def attempt(label, operation):
            try:
                return operation()
            except BaseException as error:
                errors.append(label + ': ' + repr(error))
                return None
        try:
            if self.process and self.process.stdin:
                attempt('close-handshake', self.process.stdin.close)
            def initial_active():
                active = self.job.active()
                self.evidence['active_at_cleanup_entry'] = active
                # A signaled root handle can precede Job accounting retirement.
                # Allow only the already-exited runner a bounded drain grace;
                # timeouts/interrupts terminate the still-running tree directly.
                if self.process and self.process.poll() is not None:
                    deadline = time.monotonic() + .5
                    while active and time.monotonic() < deadline:
                        time.sleep(.01)
                        active = self.job.active()
                return active
            self.evidence['active_before_termination'] = attempt('query-before', initial_active)
            if self.evidence['active_before_termination']:
                try:
                    self.evidence['remaining_job_members'] = self.job.members()
                except Exception as error:
                    self.evidence['remaining_job_members'] = {'unmeasured':repr(error)}
            members = self.evidence.get('remaining_job_members', [])
            unexpected = not isinstance(members, list)
            for member in members if isinstance(members, list) else []:
                path = member.get('image', '')
                expected = self.helpers.get(path.casefold())
                try:
                    actual = hashlib.sha256(Path(path).read_bytes()).hexdigest() if expected else None
                except OSError:
                    actual = None
                member['classification'] = 'bound-toolchain-helper' if expected and actual == expected else 'unexpected-or-unmeasured'
                member['sha256'] = actual
                unexpected |= member['classification'] != 'bound-toolchain-helper'
            self.evidence['unexpected_remaining_members'] = bool(unexpected or (self.evidence['active_before_termination'] and not members))
            attempt('terminate-job', self.job.terminate)
            # Query while holding the Job handle: closing alone cannot prove
            # all members exited. Failure to observe zero is a cleanup failure.
            def drained():
                deadline = time.monotonic() + 5
                while True:
                    active = self.job.active()
                    if active == 0:
                        self.evidence['active_after_termination'] = 0
                        return
                    if time.monotonic() >= deadline:
                        raise TimeoutError('case-job-still-active:' + str(active))
                    time.sleep(.01)
            attempt('query-drained', drained)
            attempt('wait-observed-members', self.job.wait_members)
        finally:
            attempt('close-job', self.job.close)
            if self.process:
                # Assignment failure leaves only the gated bootstrap outside
                # the Job. Terminate its retained kernel handle, never a PID scan.
                if not self.evidence['assignment_complete'] and self.process.poll() is None:
                    attempt('terminate-unassigned-bootstrap', self.process.kill)
                def root_exited():
                    self.evidence['root_exit_code'] = self.process.wait(timeout=5)
                    self.evidence['root_kernel_exited'] = True
                attempt('wait-root', root_exited)
            else:
                self.evidence['root_kernel_exited'] = True
            self.closed = True
            self.evidence['cleanup_duration_seconds'] = time.monotonic() - cleanup_started
        if errors or self.evidence['active_after_termination'] != 0 or not self.evidence['root_kernel_exited']:
            self.evidence['cleanup_errors'] = errors
            raise RuntimeError('case-tree-cleanup-unproven:' + '; '.join(errors))
