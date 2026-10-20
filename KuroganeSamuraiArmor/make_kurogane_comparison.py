from PIL import Image, ImageDraw, ImageFont
from pathlib import Path

base = Path(r"E:\Valheim Mods")
mod_assets = base / r"KuroganeSamuraiArmor\package\plugins\KuroganeSamuraiArmor\assets"
vanilla_flametal = base / r"ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FlametalArmor\model"
vanilla_feather = base / r"ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\FeatherCape\model"
vanilla_lox = base / r"ValheimUnityProject\Assets\Imported\Valheim\GameElements\Items\armor\_res\LoxCape\model"

comparisons = [
    ("Helmet diffuse", mod_assets / "samurai_helmet_d.png", vanilla_flametal / "FlametalArmor_d.png"),
    ("Helmet normal", mod_assets / "samurai_helmet_n.png", vanilla_flametal / "FlametalArmor_n.png"),
    ("Helmet mask", mod_assets / "samurai_helmet_m.png", vanilla_flametal / "FlametalArmor_m.png"),
    ("Chest diffuse", mod_assets / "samurai_chest_d.png", vanilla_flametal / "FlametalArmorChest_d.png"),
    ("Chest normal", mod_assets / "samurai_chest_n.png", vanilla_flametal / "FlametalArmorChest_n.png"),
    ("Chest mask", mod_assets / "samurai_chest_m.png", vanilla_flametal / "FlametalArmorChest_m.png"),
    ("Legs diffuse", mod_assets / "samurai_legs_d.png", vanilla_flametal / "FlametalArmorLegs_d.png"),
    ("Legs normal", mod_assets / "samurai_legs_n.png", vanilla_flametal / "FlametalArmorLegs_n.png"),
    ("Legs mask", mod_assets / "samurai_legs_m.png", None),
    ("Cloak diffuse vs Feather", mod_assets / "samurai_cloak_d.png", vanilla_feather / "feathercape_d.png"),
    ("Cloak normal vs Feather", mod_assets / "samurai_cloak_n.png", vanilla_feather / "feathercape_n.png"),
    ("Cloak mask vs Lox", mod_assets / "samurai_cloak_m.png", vanilla_lox / "LoxCape_M.png"),
]

icons = [
    ("Kabuto", mod_assets / "kurogane_kabuto_icon.png"),
    ("Do-Maru", mod_assets / "kurogane_domaru_icon.png"),
    ("Haidate", mod_assets / "kurogane_haidate_icon.png"),
    ("Jinbaori", mod_assets / "kurogane_jinbaori_icon.png"),
]

try:
    font_title = ImageFont.truetype("arial.ttf", 25)
    font = ImageFont.truetype("arial.ttf", 14)
    font_small = ImageFont.truetype("arial.ttf", 11)
except Exception:
    font_title = font = font_small = None

def load_thumb(path, max_size):
    if path is None or not Path(path).exists():
        img = Image.new("RGB", max_size, (42, 42, 42))
        return img, "no vanilla counterpart"
    raw = Image.open(path)
    dims = f"{raw.size[0]}x{raw.size[1]}"
    img = raw.convert("RGBA")
    bg = Image.new("RGBA", img.size, (45, 45, 45, 255))
    bg.alpha_composite(img)
    img = bg.convert("RGB")
    img.thumbnail(max_size, Image.Resampling.LANCZOS)
    return img, dims

# comparison sheet: columns = label, Kurogane, Vanilla
row_h = 170
label_w = 210
thumb_w = 285
pad = 16
header_h = 82
w = label_w + 2 * thumb_w + 4 * pad
h = header_h + len(comparisons) * (row_h + pad) + pad
sheet = Image.new("RGB", (w, h), (28, 30, 32))
d = ImageDraw.Draw(sheet)
d.text((pad, 18), "Kurogane Generated Textures vs Valheim Vanilla UV Targets", fill=(238, 238, 232), font=font_title)
d.text((label_w + pad*2, 55), "Kurogane texture", fill=(210, 215, 220), font=font)
d.text((label_w + thumb_w + pad*3, 55), "Closest Valheim vanilla texture", fill=(210, 215, 220), font=font)

for i, (label, k_path, v_path) in enumerate(comparisons):
    y = header_h + i * (row_h + pad)
    d.rectangle((pad, y, w-pad, y+row_h), outline=(65,68,72))
    d.text((pad+8, y+12), label, fill=(245,245,240), font=font)

    k_img, k_dims = load_thumb(k_path, (thumb_w-20, row_h-48))
    v_img, v_dims = load_thumb(v_path, (thumb_w-20, row_h-48))
    kx = label_w + pad*2 + (thumb_w - k_img.width)//2
    vx = label_w + thumb_w + pad*3 + (thumb_w - v_img.width)//2
    iy = y + 10 + (row_h-48 - k_img.height)//2
    sheet.paste(k_img, (kx, iy))
    sheet.paste(v_img, (vx, y + 10 + (row_h-48 - v_img.height)//2))
    d.text((label_w + pad*2 + 6, y+row_h-32), f"{Path(k_path).name}  {k_dims}", fill=(170,174,178), font=font_small)
    if v_path:
        vname = Path(v_path).name
    else:
        vname = "none"
    d.text((label_w + thumb_w + pad*3 + 6, y+row_h-32), f"{vname}  {v_dims}", fill=(170,174,178), font=font_small)

out1 = base / r"KuroganeSamuraiArmor\kurogane_vs_valheim_texture_comparison.png"
sheet.save(out1)

# icon sheet
icon_w, icon_h = 190, 170
w2 = len(icons) * icon_w + (len(icons)+1)*pad
h2 = 245
sheet2 = Image.new("RGB", (w2, h2), (28,30,32))
d2 = ImageDraw.Draw(sheet2)
d2.text((pad, 18), "Kurogane Item Icons", fill=(238,238,232), font=font_title)
for i, (label, p) in enumerate(icons):
    x = pad + i*(icon_w+pad)
    y = 70
    d2.rectangle((x, y, x+icon_w, y+icon_h), outline=(65,68,72))
    img, dims = load_thumb(p, (140, 110))
    sheet2.paste(img, (x + (icon_w-img.width)//2, y+12))
    d2.text((x+8, y+126), label, fill=(245,245,240), font=font)
    d2.text((x+8, y+145), f"{p.name}  {dims}", fill=(170,174,178), font=font_small)
out2 = base / r"KuroganeSamuraiArmor\kurogane_icon_sheet.png"
sheet2.save(out2)

print(out1)
print(out2)
