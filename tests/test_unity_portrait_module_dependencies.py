"""Guard Unity 6 built-in package dependencies used by runtime, editor and tests."""

import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "Packages/manifest.json"
CS_FILES = ROOT / "Assets"


class UnityPortraitModuleDependenciesTests(unittest.TestCase):
    def test_every_used_builtin_engine_module_is_declared(self):
        # Read the complete Unity C# surface so package coverage does not
        # regress when APIs move between runtime/editor/test assemblies.
        source = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(CS_FILES.rglob("*.cs"))
        )
        modules = json.loads(MANIFEST.read_text(encoding="utf-8"))["dependencies"]
        features = {
            r"\bUnityWebRequest\b": "com.unity.modules.unitywebrequest",
            r"\bImageConversion\b": "com.unity.modules.imageconversion",
            r"\bJsonUtility\b": "com.unity.modules.jsonserialize",
            r"\bGUI(?:Layout|Style)?\b": "com.unity.modules.imgui",
        }
        for api, package in features.items():
            if re.search(api, source):
                self.assertEqual("1.0.0", modules.get(package),
                                 f"{api} requires Unity built-in module {package}")


if __name__ == "__main__":
    unittest.main()
