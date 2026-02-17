"""
Create / remove Windows shortcuts for Local Dictation.

Usage:
    python install.py              # install shortcuts
    python install.py --uninstall  # remove shortcuts
"""

import os
import subprocess
import sys
from pathlib import Path

APP_NAME = "Local Dictation"
SCRIPT_DIR = Path(__file__).resolve().parent
DICTATION_SCRIPT = SCRIPT_DIR / "dictation.py"
ICON_FILE = SCRIPT_DIR / "icon.ico"


def _pythonw() -> str:
    """Return path to pythonw.exe next to the current interpreter."""
    base = Path(sys.executable).parent
    pythonw = base / "pythonw.exe"
    if pythonw.exists():
        return str(pythonw)
    # Fallback: same directory but Scripts (venv layout)
    pythonw_scripts = base / "Scripts" / "pythonw.exe"
    if pythonw_scripts.exists():
        return str(pythonw_scripts)
    # Last resort
    return "pythonw.exe"


def _shell_folder(name: str) -> Path:
    """Resolve a Windows shell folder path (Desktop, StartMenu, Startup)."""
    result = subprocess.run(
        ["powershell", "-NoProfile", "-Command",
         f"[Environment]::GetFolderPath('{name}')"],
        capture_output=True, text=True, check=True,
    )
    return Path(result.stdout.strip())


def _create_shortcut(lnk_path: Path, target: str, arguments: str,
                     working_dir: str, icon_location: str | None):
    """Create a .lnk shortcut using PowerShell / WScript.Shell."""
    ps_lines = [
        "$ws = New-Object -ComObject WScript.Shell",
        f"$s = $ws.CreateShortcut('{lnk_path}')",
        f"$s.TargetPath = '{target}'",
        f"$s.Arguments = '\"{arguments}\"'",
        f"$s.WorkingDirectory = '{working_dir}'",
    ]
    if icon_location:
        ps_lines.append(f"$s.IconLocation = '{icon_location}'")
    ps_lines.append("$s.Save()")
    script = "; ".join(ps_lines)
    subprocess.run(
        ["powershell", "-NoProfile", "-Command", script],
        check=True,
    )


OPTIONS = [
    ("Desktop shortcut", "Desktop", lambda: _shell_folder("Desktop") / f"{APP_NAME}.lnk"),
    ("Start Menu shortcut", "StartMenu", lambda: _shell_folder("StartMenu") / "Programs" / f"{APP_NAME}.lnk"),
    ("Autostart at logon", "Startup", lambda: _shell_folder("Startup") / f"{APP_NAME}.lnk"),
]


def _prompt_choices() -> list[int]:
    """Ask the user yes/no for each option. Returns list of indices."""
    print("Select which shortcuts to install:\n")
    chosen = []
    for i, (label, _, _) in enumerate(OPTIONS):
        while True:
            answer = input(f"  {label}? [Y/n] ").strip().lower()
            if answer in ("", "y", "yes"):
                chosen.append(i)
                break
            elif answer in ("n", "no"):
                break
            else:
                print("    Please enter y or n.")
    return chosen


def install():
    chosen = _prompt_choices()
    if not chosen:
        print("Nothing selected.")
        return

    target = _pythonw()
    arguments = str(DICTATION_SCRIPT)
    working_dir = str(SCRIPT_DIR)
    icon_location = str(ICON_FILE) if ICON_FILE.exists() else None

    print()
    for i in chosen:
        label, _, path_fn = OPTIONS[i]
        lnk_path = path_fn()
        lnk_path.parent.mkdir(parents=True, exist_ok=True)
        _create_shortcut(lnk_path, target, arguments, working_dir,
                         icon_location)
        print(f"  Created {label}: {lnk_path}")

    print(f"\nInstalled. pythonw = {target}")
    if icon_location:
        print(f"Icon: {icon_location}")
    else:
        print("No icon.ico found — shortcuts use default icon.")


def uninstall():
    shortcuts = [
        (_shell_folder("Desktop") / f"{APP_NAME}.lnk", "Desktop"),
        (_shell_folder("StartMenu") / "Programs" / f"{APP_NAME}.lnk",
         "Start Menu"),
        (_shell_folder("Startup") / f"{APP_NAME}.lnk", "Startup"),
    ]
    for lnk_path, label in shortcuts:
        if lnk_path.exists():
            lnk_path.unlink()
            print(f"  Removed {label} shortcut: {lnk_path}")
        else:
            print(f"  {label} shortcut not found (skipped)")
    print("\nUninstalled.")


def main():
    if "--uninstall" in sys.argv:
        uninstall()
    else:
        install()


if __name__ == "__main__":
    main()
