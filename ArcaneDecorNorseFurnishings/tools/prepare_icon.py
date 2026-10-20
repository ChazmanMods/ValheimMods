from PIL import Image,ImageDraw
from pathlib import Path
root=Path('ArcaneDecorNorseFurnishings')
source=Image.open(r'C:/Users/Charles Sammons/.codex/generated_images/01a12d14-af95-7603-9de8-2ca69f76d67c/exec-45b4db47-510c-4584-9458-cf7925c4952a.png').convert('RGBA')
assert all(source.getpixel(p)[3]==0 for p in [(0,0),(source.width-1,0),(0,source.height-1),(source.width-1,source.height-1)])
source.save(root/'Assets/icon-master.png')
source.resize((256,256),Image.Resampling.LANCZOS).save(root/'icon.png')
preview=Image.new('RGB',(768,256))
for col,color in enumerate(['white','#181818','#aaaaaa']):
 bg=Image.new('RGBA',(256,256),color)
 if col==2:
  draw=ImageDraw.Draw(bg)
  for y in range(0,256,16):
   for x in range(0,256,16):
    if (x//16+y//16)%2:draw.rectangle((x,y,x+15,y+15),fill='#eeeeee')
 bg.alpha_composite(Image.open(root/'icon.png'));preview.paste(bg.convert('RGB'),(col*256,0))
preview.save(root/'Assets/icon-background-review.png')
print('Master alpha and corners verified; 256x256 PNG and review sheet written.')
