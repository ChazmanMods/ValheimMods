# Plumbing asset tools

The shipped meshes, textures and rendered icons are in `../../Assets/Plumbing`.

To rebuild from the four user-supplied AW_Copper ZIP archives, run `extract.py` with Python (the input folder defaults to the user Downloads path in that script). Run `preview_models.py`, `export_models.py` and then `variant_icons.py` with Blender in background mode. The scripts resolve output paths relative to this folder. Inspection helpers are included for model bounds and socket positions.

No model generation is needed for a normal .NET build.
