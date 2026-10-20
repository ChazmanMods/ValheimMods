from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

items = [
    ("Flametal armor diffuse", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmor_d.png"),
    ("Flametal armor normal", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmor_n.png"),
    ("Flametal armor mask", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmor_m.png"),
    ("Flametal armor emission", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmor_e.png"),
    ("Flametal chest diffuse", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmorChest_d.png"),
    ("Flametal chest normal", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmorChest_n.png"),
    ("Flametal chest mask", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmorChest_m.png"),
    ("Flametal legs diffuse", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmorLegs_d.png"),
    ("Flametal legs normal", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model\FlametalArmorLegs_n.png"),
    ("Feather cape diffuse", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FeatherCape\model\feathercape_d.png"),
    ("Feather cape normal", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FeatherCape\model\feathercape_n.png"),
    ("Lox cape diffuse", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\LoxCape\model\LoxCape_D.png"),
    ("Lox cape normal", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\LoxCape\model\LoxCape_N.png"),
    ("Lox cape mask", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\LoxCape\model\LoxCape_M.png"),
    ("Ash cape diffuse", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\Ashcape\model\Ashcape_d.png"),
    ("Ash cape mask", r"E:\Valheim Mods\ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\Ashcape\model\Ashcape_m.png"),
]

out = Path(r"E:\Valheim Mods\KuroganeSamuraiArmor\reference_valheim_paint_targets.png")
thumb_w, thumb_h = 220, 170
label_h = 48
cols = 4
rows = (len(items) + cols - 1) // cols
pad = 18
header_h = 68
w = cols * thumb_w + (cols + 1) * pad
h = header_h + rows * (thumb_h + label_h + pad) + pad
sheet = Image.new("RGB", (w, h), (30, 32, 34))
d = ImageDraw.Draw(sheet)
try:
    font_title = ImageFont.truetype("arial.ttf", 26)
    font = ImageFont.truetype("arial.ttf", 14)
    font_small = ImageFont.truetype("arial.ttf", 11)
except Exception:
    font_title = font = font_small = None

d.text((pad, 18), "Valheim Armor Paint Targets: Kurogane base references", fill=(235,235,230), font=font_title)
for i, (label, path) in enumerate(items):
    p = Path(path)
    col = i % cols
    row = i // cols
    x = pad + col * (thumb_w + pad)
    y = header_h + row * (thumb_h + label_h + pad)
    d.rectangle((x-1, y-1, x+thumb_w+1, y+thumb_h+label_h+1), outline=(70,72,75))
    try:
        img = Image.open(p).convert("RGBA")
        bg = Image.new("RGBA", img.size, (44,44,44,255))
        bg.alpha_composite(img)
        img = bg.convert("RGB")
        img.thumbnail((thumb_w, thumb_h), Image.Resampling.LANCZOS)
        ix = x + (thumb_w - img.width)//2
        iy = y + (thumb_h - img.height)//2
        sheet.paste(img, (ix, iy))
        dims = f"{Image.open(p).size[0]}x{Image.open(p).size[1]}"
    except Exception as e:
        dims = "missing"
        d.text((x+8,y+8), str(e), fill=(255,120,120), font=font_small)
    d.text((x+6, y+thumb_h+6), label, fill=(235,235,230), font=font)
    d.text((x+6, y+thumb_h+25), f"{p.name}  {dims}", fill=(170,174,178), font=font_small)

sheet.save(out)
print(out)
