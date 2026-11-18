"""Read test results from the TRX files of a spec-suite run.

Shared by compare-baseline.py and root-causes.py so that both count the same tests. run-suites.sh
writes the TRX files directly into its output directory, so only that directory is searched, not
its subdirectories.
"""
import glob
import os
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def read_trx(results_dir):
    """Yield (test name, outcome, error message) for each result in the directory's TRX files."""
    for path in sorted(glob.glob(os.path.join(results_dir, "*.trx"))):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        for result in root.iterfind(".//t:UnitTestResult", NS):
            message = result.find("t:Output/t:ErrorInfo/t:Message", NS)
            text = (message.text or "") if message is not None else ""
            yield result.get("testName"), result.get("outcome"), text
