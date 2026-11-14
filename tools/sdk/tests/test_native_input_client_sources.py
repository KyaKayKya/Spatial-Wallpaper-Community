import ast
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[3]
SPEC=importlib.util.spec_from_file_location('native_input_client_sources',ROOT/'Supervisor/Tests/native_input_client_sources.py')
MODULE=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(MODULE)


class NativeClientSources(unittest.TestCase):
    def test_real_inventory_complete_unique_and_preview_separate(self):
        desktop=MODULE.native_input_client_sources(ROOT)
        names=[p.name for p in desktop]
        for name in ('NativeDesktopInputCandidateChecks.cs','NativeHostLifecycle.cs','OwnedThreadEvidence.cs'):
            self.assertEqual(names.count(name),1)
        self.assertEqual(len(desktop),len(set(desktop)))
        preview=MODULE.native_input_client_sources(ROOT,desktop=False)
        self.assertIn(ROOT/'Supervisor/Tests/NativeInputHostChecks.cs',preview)
        self.assertFalse(any(p.name.startswith('NativeDesktopInput') for p in preview))

    def test_new_partial_and_missing_support(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            for original in MODULE.native_input_client_sources(ROOT):
                target=root/original.relative_to(ROOT);target.parent.mkdir(parents=True,exist_ok=True);target.touch()
            extra=root/'Supervisor/Tests/NativeDesktopInputFutureChecks.cs';extra.touch()
            self.assertIn(extra,MODULE.native_input_client_sources(root))
            (root/'Supervisor/Tests/OwnedThreadEvidence.cs').unlink()
            with self.assertRaisesRegex(FileNotFoundError,'OwnedThreadEvidence'):
                MODULE.native_input_client_sources(root)

    def test_all_compilers_use_shared_inventory(self):
        for name in ('compile_input_continuity.py','run_native_lifecycle.py','run_native_input_host.py'):
            tree=ast.parse((ROOT/'Supervisor/Tests'/name).read_text(encoding='utf-8-sig'))
            calls=[n for n in ast.walk(tree) if isinstance(n,ast.Call) and isinstance(n.func,ast.Name)
                   and n.func.id=='native_input_client_sources']
            self.assertEqual(len(calls),1,name)


if __name__=='__main__':unittest.main()
