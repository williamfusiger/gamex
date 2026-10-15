#!/usr/bin/env python3
"""Download the forest asset kit named in the manifest into the Unity project.

usage: python3 -I fetch_assets.py <manifest.json> <project root> [--milestone M1..M6] [--id <asset id>]...
                                  [--dry-run]

  <manifest.json>   Assets/Game/Editor/Forest/forest-asset-manifest.json of the project
  <project root>    the folder that holds Assets/ and ProjectSettings/
  --milestone Mn    fetch every asset up to and including that milestone. Default M4 (version 1),
                    so the deferred skies (M5) and rock faces (M6) are fetched only when asked for
  --id <asset id>   fetch only this asset, whatever its milestone. May be given more than once
  --dry-run         say what would be fetched and what is already in place; download nothing

Each file goes to <project root>/Assets/ThirdParty/<relPath>. A file that is already there with
the manifest's byte size and md5 is skipped, so an interrupted run is simply started again.

Rules this follows (forest specification, section 5.1):
  - one file at a time, whole files only, through curl with a size cap just above the expected
    size. No HTTP ranges: the server once answered a range request with a 559 MB body.
  - download to <file>.part, check byte size and md5 against the manifest, then rename.
  - Poly Haven asks for a User-Agent that names the software.
  - after a model's FBX is in place, fbx_objects.py measures it against the manifest. On a
    mismatch the fetch stops with exit status 2 and changes nothing: no prefab may ever be built
    from sizes that were not measured on the file that was downloaded.

Exit status: 0 everything is in place and measured, 1 a download or check failed,
2 a model does not match the manifest.

Python standard library only. Downloaded files are data: nothing in them is executed.
"""

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time

USER_AGENT = "gamex-forest-builder/1.0"
ALLOWED_URL_PREFIX = "https://dl.polyhaven.org/"   # URLs come from Poly Haven's /files API, never built by hand
THIRD_PARTY = ("Assets", "ThirdParty")
REQUIRED_TOP_FOLDER = "PolyHaven"
DEFAULT_MILESTONE = "M4"

EXIT_OK = 0
EXIT_ERROR = 1
EXIT_MISMATCH = 2


class FetchError(Exception):
    """Stops the run with exit status 1."""


class Arguments(argparse.ArgumentParser):
    def error(self, message):
        # argparse would exit with 2, which this tool reserves for "a model does not match".
        self.print_usage(sys.stderr)
        sys.stderr.write("error: %s\n" % message)
        sys.exit(EXIT_ERROR)


def milestone_number(label):
    match = re.fullmatch(r"M([0-9]+)", str(label))
    if match is None:
        raise FetchError("not a milestone: %r (expected M1 to M6)" % (label,))
    return int(match.group(1))


def say(message):
    print(message, flush=True)


def megabytes(byte_count):
    return "%.1f MB" % (byte_count / 1e6)


def md5_of(path):
    digest = hashlib.md5()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def difference(path, expected_bytes, expected_md5):
    """Why the file at path is not the manifest's file, or "" when it is."""
    actual_bytes = os.path.getsize(path)
    if actual_bytes != expected_bytes:
        return "%d bytes, expected %d" % (actual_bytes, expected_bytes)
    actual_md5 = md5_of(path)
    if actual_md5 != expected_md5:
        return "md5 %s, expected %s" % (actual_md5, expected_md5)
    return ""


def load_manifest(path):
    try:
        with open(path, "r", encoding="utf-8") as handle:
            manifest = json.load(handle)
    except (OSError, ValueError) as error:
        raise FetchError("cannot read the manifest %s: %s" % (path, error))
    if not isinstance(manifest, list) or not manifest:
        raise FetchError("the manifest %s is not a non-empty JSON array" % path)

    seen_ids = set()
    seen_paths = set()
    for entry in manifest:
        if not isinstance(entry, dict) or not isinstance(entry.get("id"), str) or not entry["id"]:
            raise FetchError("a manifest entry has no id")
        asset_id = entry["id"]
        if asset_id in seen_ids:
            raise FetchError("the manifest lists %s twice" % asset_id)
        seen_ids.add(asset_id)
        milestone_number(entry.get("milestone"))
        files = entry.get("files")
        if not isinstance(files, list) or not files:
            raise FetchError("%s has no files" % asset_id)
        for item in files:
            check_file_record(asset_id, item)
            if item["relPath"] in seen_paths:
                raise FetchError("two files share the destination %s" % item["relPath"])
            seen_paths.add(item["relPath"])
    return manifest


def check_file_record(asset_id, item):
    """A manifest is an input like any other: refuse anything that could write outside the kit folder."""
    if not isinstance(item, dict):
        raise FetchError("%s: a file record is not an object" % asset_id)
    url, rel_path, size, md5 = item.get("url"), item.get("relPath"), item.get("bytes"), item.get("md5")
    if not isinstance(url, str) or not url.startswith(ALLOWED_URL_PREFIX) or any(c.isspace() for c in url):
        raise FetchError("%s: URL %r is not under %s" % (asset_id, url, ALLOWED_URL_PREFIX))
    if not isinstance(rel_path, str) or not rel_path or "\\" in rel_path or rel_path.startswith("/"):
        raise FetchError("%s: bad destination %r" % (asset_id, rel_path))
    parts = rel_path.split("/")
    if parts[0] != REQUIRED_TOP_FOLDER or len(parts) < 3 or any(part in ("", ".", "..") for part in parts):
        raise FetchError("%s: destination %r is not a file under %s/<id>/" % (asset_id, rel_path, REQUIRED_TOP_FOLDER))
    if rel_path.endswith(".part") or rel_path.endswith(".meta"):
        raise FetchError("%s: destination %r has a reserved extension" % (asset_id, rel_path))
    if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
        raise FetchError("%s: %s has no byte size" % (asset_id, rel_path))
    if not isinstance(md5, str) or re.fullmatch(r"[0-9a-f]{32}", md5) is None:
        raise FetchError("%s: %s has no md5" % (asset_id, rel_path))


def select(manifest, milestone, ids):
    """The entries to fetch, in manifest order."""
    known = {entry["id"] for entry in manifest}
    for asset_id in ids:
        if asset_id not in known:
            raise FetchError("the manifest has no asset with id %s" % asset_id)
    if ids:
        # An asset named by id is wanted, whatever its milestone; an explicit --milestone still narrows.
        limit = milestone_number(milestone) if milestone is not None else None
        return [entry for entry in manifest
                if entry["id"] in ids and (limit is None or milestone_number(entry["milestone"]) <= limit)]
    limit = milestone_number(milestone if milestone is not None else DEFAULT_MILESTONE)
    return [entry for entry in manifest if milestone_number(entry["milestone"]) <= limit]


def download(url, part_path, expected_bytes):
    """Runs curl. Returns its exit status; the caller verifies what arrived."""
    command = [
        "curl", "--fail", "--location", "--retry", "3",
        "--user-agent", USER_AGENT,
        "--max-filesize", str(expected_bytes + 1024),
        "--proto", "=https", "--proto-redir", "=https",
        "--connect-timeout", "30",
        "--speed-limit", "1024", "--speed-time", "120",   # give up on a transfer that has stalled for two minutes
        "--silent", "--show-error",
        "--output", part_path,
        url,
    ]
    try:
        return subprocess.run(command, stdin=subprocess.DEVNULL).returncode
    except OSError as error:
        raise FetchError("cannot run curl: %s" % error)


def remove_quietly(path):
    try:
        os.remove(path)
    except OSError:
        pass


def fetch_file(item, destination, label, dry_run, totals):
    """Makes one manifest file be in place. Returns True when it is (or, in a dry run, would be)."""
    expected_bytes, expected_md5 = item["bytes"], item["md5"]
    stale = ""
    if os.path.isfile(destination):
        stale = difference(destination, expected_bytes, expected_md5)
        if not stale:
            say("%s ok      %s (%s)" % (label, item["relPath"], megabytes(expected_bytes)))
            totals["present"] += 1
            return True
    elif os.path.exists(destination):
        raise FetchError("%s exists and is not a file" % destination)

    if dry_run:
        reason = "is missing" if not stale else "differs: " + stale
        say("%s fetch   %s (%s) %s" % (label, item["relPath"], megabytes(expected_bytes), reason))
        totals["missing"] += 1
        totals["missing_bytes"] += expected_bytes
        return False

    if stale:
        say("%s replace %s: the file in place has %s" % (label, item["relPath"], stale))
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    part_path = destination + ".part"
    started = time.time()
    try:
        status = download(item["url"], part_path, expected_bytes)
        if status != 0:
            raise FetchError("curl failed with status %d for %s" % (status, item["url"]))
        problem = difference(part_path, expected_bytes, expected_md5) if os.path.isfile(part_path) else "nothing was written"
        if problem:
            raise FetchError("%s arrived with %s. The file on the server is not the one the manifest describes; "
                             "nothing was kept" % (item["url"], problem))
        os.replace(part_path, destination)
    finally:
        remove_quietly(part_path)   # gone already after a successful rename; never left behind otherwise
    seconds = max(time.time() - started, 0.001)
    say("%s fetched %s (%s in %.1f s, md5 ok)" % (label, item["relPath"], megabytes(expected_bytes), seconds))
    totals["fetched"] += 1
    totals["fetched_bytes"] += expected_bytes
    return True


def measure(measure_tool, fbx_path, manifest_path, asset_id):
    """Runs fbx_objects.py as its own process (isolated mode keeps the script folder off the import path)."""
    interpreter = sys.executable or "python3"
    sys.stdout.flush()
    try:
        return subprocess.run([interpreter, "-I", measure_tool, fbx_path, manifest_path, asset_id],
                              stdin=subprocess.DEVNULL).returncode
    except OSError as error:
        raise FetchError("cannot run %s: %s" % (measure_tool, error))


def run(arguments):
    manifest_path = os.path.abspath(arguments.manifest)
    root = os.path.abspath(arguments.project_root)
    if not os.path.isdir(os.path.join(root, "Assets")) or not os.path.isdir(os.path.join(root, "ProjectSettings")):
        raise FetchError("%s is not a Unity project (no Assets/ and ProjectSettings/)" % root)
    measure_tool = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fbx_objects.py")
    if not os.path.isfile(measure_tool):
        raise FetchError("the measuring tool is missing: %s" % measure_tool)

    manifest = load_manifest(manifest_path)
    entries = select(manifest, arguments.milestone, arguments.id or [])
    file_count = sum(len(entry["files"]) for entry in entries)
    byte_count = sum(item["bytes"] for entry in entries for item in entry["files"])
    say("%d of %d assets selected: %d files, %s -> %s"
          % (len(entries), len(manifest), file_count, megabytes(byte_count), os.path.join(root, *THIRD_PARTY)))
    if not entries:
        say("Nothing to do.")
        return EXIT_OK

    totals = {"present": 0, "fetched": 0, "fetched_bytes": 0, "missing": 0, "missing_bytes": 0}
    measured = 0
    index = 0
    for entry in entries:
        asset_id = entry["id"]
        # The mesh first: if it does not match the manifest, its textures need not be fetched yet.
        files = sorted(entry["files"], key=lambda item: 0 if item.get("map") == "mesh" else 1)
        for item in files:
            index += 1
            label = "[%*d/%d]" % (len(str(file_count)), index, file_count)
            destination = os.path.join(root, *THIRD_PARTY, *item["relPath"].split("/"))
            in_place = fetch_file(item, destination, label, arguments.dry_run, totals)
            if item.get("map") == "mesh" and entry.get("kind") == "model" and in_place and not arguments.dry_run:
                status = measure(measure_tool, destination, manifest_path, asset_id)
                if status == EXIT_MISMATCH:
                    say("STOPPED: %s does not match the manifest. Nothing was changed. Check that the layout "
                          "still holds with the measured sizes, then store them with:" % asset_id)
                    say("  python3 -I %s %s %s %s --write" % (measure_tool, destination, manifest_path, asset_id))
                    say("and run this fetch again.")
                    return EXIT_MISMATCH
                if status != 0:
                    raise FetchError("measuring %s failed with status %d" % (destination, status))
                measured += 1

    if arguments.dry_run:
        say("Dry run: %d files in place, %d to fetch (%s). Nothing was downloaded or measured."
              % (totals["present"], totals["missing"], megabytes(totals["missing_bytes"])))
        return EXIT_OK
    say("Done: %d files fetched (%s), %d already in place, %d models measured against the manifest."
          % (totals["fetched"], megabytes(totals["fetched_bytes"]), totals["present"], measured))
    return EXIT_OK


def main():
    parser = Arguments(description="Download the forest asset kit named in the manifest into the Unity project. "
                                   "Exit status: 0 done, 1 failure, 2 a model does not match the manifest.")
    parser.add_argument("manifest", help="forest-asset-manifest.json of the project")
    parser.add_argument("project_root", metavar="project-root", help="the folder that holds Assets/")
    parser.add_argument("--milestone", metavar="Mn", default=None,
                        help="fetch every asset up to and including this milestone (default %s)" % DEFAULT_MILESTONE)
    parser.add_argument("--id", action="append", metavar="ASSET",
                        help="fetch only this asset id, whatever its milestone; may be repeated")
    parser.add_argument("--dry-run", action="store_true", help="list what would be fetched; download nothing")
    arguments = parser.parse_args()
    try:
        return run(arguments)
    except FetchError as error:
        sys.stdout.flush()
        sys.stderr.write("error: %s\n" % error)
        return EXIT_ERROR
    except KeyboardInterrupt:
        sys.stderr.write("interrupted. Run again to continue: files already in place are skipped.\n")
        return EXIT_ERROR


if __name__ == "__main__":
    sys.exit(main())
