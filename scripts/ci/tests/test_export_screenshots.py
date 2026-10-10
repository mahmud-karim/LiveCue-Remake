import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from export_screenshots import readable_name, rename_from_manifest  # noqa: E402

UUID = "0A1B2C3D-0000-4000-8000-00000000ABCD"


class ReadableNameTests(unittest.TestCase):
    def test_uses_the_prefix_test_and_attachment_name(self):
        attachment = {"exportedFileName": f"{UUID}.png", "suggestedHumanReadableName": f"placeholder_0_{UUID}.png"}
        self.assertEqual(
            readable_name("ios26", "PlaceholderUITests/testPlaceholder()", attachment),
            "ios26-PlaceholderUITests-testPlaceholder-placeholder.png",
        )

    def test_falls_back_to_the_exported_name(self):
        self.assertEqual(readable_name("", "", {"exportedFileName": "abc.PNG"}), "abc.png")


class RenameFromManifestTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)

    def write(self, name, content=b"png"):
        with open(os.path.join(self.folder.name, name), "wb") as handle:
            handle.write(content)

    def test_renames_listed_files_and_keeps_names_unique(self):
        attachments = []
        for index in range(2):
            exported = f"file{index}.png"
            self.write(exported)
            attachments.append({"exportedFileName": exported, "suggestedHumanReadableName": f"shot_{index}_{UUID}.png"})
        attachments.append({"exportedFileName": "missing.png", "suggestedHumanReadableName": f"gone_0_{UUID}.png"})
        self.write("notes.txt")
        self.write("manifest.json", json.dumps([{"testIdentifier": "Suite/test()", "attachments": attachments}]).encode())

        images = rename_from_manifest(self.folder.name, "ios18")

        self.assertEqual(images, ["ios18-Suite-test-shot-2.png", "ios18-Suite-test-shot.png"])

    def test_lists_images_when_there_is_no_manifest(self):
        self.write("b.png")
        self.write("a.jpg")
        self.write("manifest.txt")
        self.assertEqual(rename_from_manifest(self.folder.name, "x"), ["a.jpg", "b.png"])


if __name__ == "__main__":
    unittest.main()
