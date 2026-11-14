import unittest
from tools.market.prepare_public_source import category

class SourceProfileTests(unittest.TestCase):
    def test_real_submission_template_is_public(self):
        self.assertIsNone(category('.github/PULL_REQUEST_TEMPLATE/market.md'))
        self.assertIsNone(category('LICENSE'))
        self.assertIsNone(category('PROJECT_LICENSE_SCOPE.md'))
        self.assertEqual(category('.github/PULL_REQUEST_TEMPLATE/private.md'),'outside-source-allowlist')
        self.assertEqual(category('.github/workflows/unrelated.yml'),'outside-source-allowlist')

if __name__ == '__main__': unittest.main()
