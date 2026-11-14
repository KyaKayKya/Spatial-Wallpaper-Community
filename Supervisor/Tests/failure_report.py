"""Persist acceptance diagnostics without replacing an already active failure."""
import json
import sys
from contextlib import contextmanager


@contextmanager
def failure_receipt(path, context):
    """Preserve failed-run identity even when validation never reaches results."""
    try:
        yield
    except BaseException as error:
        report = dict(context)
        report.update(status='failed', primary_failure=repr(error))
        save_report(path, report, error)
        raise


def save_report(path, report, primary_failure=None, stderr=None):
    try:
        path.write_text(json.dumps(report, indent=2))
    except Exception as error:
        # Both fallback channels are best effort. Neither owns the primary exception.
        if primary_failure is not None:
            try:
                primary_failure.report_save_error = repr(error)
            except Exception:
                pass
        try:
            (sys.stderr if stderr is None else stderr).write('report-save-failure: ' + repr(error) + '\n')
        except Exception:
            pass
        if primary_failure is None:
            raise
