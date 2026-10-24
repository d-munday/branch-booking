"""Create a portable source archive without secrets, dependencies or generated files."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
target = root.parent / "branch-booking-source.zip"
excluded = {".git", ".local", ".vs", "artifacts", "bin", "obj", "node_modules", "dist", "coverage", "TestResults", "__pycache__"}
files = []
for path in root.rglob("*"):
    relative = path.relative_to(root)
    if not path.is_file() or excluded.intersection(relative.parts):
        continue
    if (path.name.startswith(".env") and path.name != ".env.example") or path.suffix == ".tsbuildinfo":
        continue
    files.append(path)

with ZipFile(target, "w", ZIP_DEFLATED) as archive:
    for path in sorted(files):
        archive.write(path, Path(root.name) / path.relative_to(root))

print(f"Created {target}: {len(files)} source files, {target.stat().st_size:,} bytes")
