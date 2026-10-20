"""Mandatory transparent oval-icon gate. Usage: python tools/check_icon_backgrounds.py FILE..."""
import io, sys, zipfile
from pathlib import Path
from PIL import Image

def check(path):
    data = zipfile.ZipFile(path).read("icon.png") if path.suffix.lower() == ".zip" else path.read_bytes()
    im = Image.open(io.BytesIO(data))
    assert im.format == "PNG" and im.size == (256,256), "icon must be a 256x256 PNG"
    assert "A" in im.getbands() or "transparency" in im.info, "no alpha transparency"
    rgba = im.convert("RGBA"); alpha = rgba.getchannel("A")
    assert all(alpha.getpixel(p)==0 for p in [(0,0),(255,0),(0,255),(255,255)]), "opaque corner background"
    clear = sum(a==0 for a in alpha.getdata())
    assert 6553 <= clear < 65000, "missing meaningful transparent surroundings or empty artwork"
    assert alpha.getextrema()[1]==255, "no opaque artwork"
    print(f"PASS {path}: PNG 256x256; {clear}/65536 fully transparent pixels; clear corners")

if __name__ == "__main__":
    if len(sys.argv)<2: raise SystemExit("Provide final ZIP or PNG paths")
    errors=[]
    for value in sys.argv[1:]:
        try: check(Path(value))
        except Exception as e: errors.append(f"FAIL {value}: {e}")
    if errors: raise SystemExit("\n".join(errors))
