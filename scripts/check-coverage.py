"""Enforce aggregate Cobertura coverage for all production backend assemblies."""
import argparse
import pathlib
import sys
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("root", nargs="?", default="artifacts/backend")
parser.add_argument("--minimum", type=float, default=80)
args = parser.parse_args()
reports = sorted(pathlib.Path(args.root).rglob("*.cobertura.xml"))
if not reports:
    sys.exit(f"No Cobertura coverage reports under {args.root}")

if len(reports) != 1:
    sys.exit("Expected one fresh coverage report. Clear the results directory before running tests.")

root = ET.parse(reports[0]).getroot()
rates = {
    "line": float(root.attrib["line-rate"]) * 100,
    "branch": float(root.attrib["branch-rate"]) * 100,
}
for kind, rate in rates.items():
    print(f"Backend {kind} coverage: {rate:.2f}% (required {args.minimum:.0f}%)")
if any(rate < args.minimum for rate in rates.values()):
    sys.exit(1)

packages = {package.attrib["name"] for package in root.findall("./packages/package")}
required = {"Booking.Core", "Booking.CommandApi", "Booking.QueryApi"}
missing = required - packages
if missing:
    sys.exit(f"Missing production assemblies in coverage: {', '.join(sorted(missing))}")
