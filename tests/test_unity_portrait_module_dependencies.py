"""Guard Unity 6 built-in package dependencies for the shared portrait loader."""

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "Packages/manifest.json"
PORTRAITS = ROOT / "Assets/Scripts/Unity/OrdrCharacterPortraits.cs"


class UnityPortraitModuleDependenciesTests(unittest.TestCase):
    def test_loader_has_required_engine_modules(self):
        source = PORTRAITS.read_text(encoding="utf-8")
        modules = json.loads(MANIFEST.read_text(encoding="utf-8"))["dependencies"]
        features = {
            "UnityWebRequest": "com.unity.modules.unitywebrequest",
            "ImageConversion": "com.unity.modules.imageconversion",
        }
        for api, package in features.items():
            if api in source:
                self.assertEqual("1.0.0", modules.get(package),
                                 f"{api} requires Unity built-in module {package}")


if __name__ == "__main__":
    unittest.main()
