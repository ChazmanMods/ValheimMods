from pathlib import Path
p=Path('ArcaneDecorNorseFurnishings/Plugin.cs');s=p.read_text()
s=s.replace('for(int i=0;i<ingredients.Length;i++)config.AddRequirement(ingredients[i],amounts[i],true);\n    if(!PieceManager.Instance.AddPiece(new CustomPiece(root,false,config)))', '''var custom=new CustomPiece(root,false,config);
    var requirements=new Piece.Requirement[ingredients.Length];
    for(int i=0;i<ingredients.Length;i++)requirements[i]=new Piece.Requirement{m_resItem=PrefabManager.Instance.GetPrefab(ingredients[i]).GetComponent<ItemDrop>(),m_amount=amounts[i],m_recover=true};
    root.GetComponent<Piece>().m_resources=requirements;
    if(!PieceManager.Instance.AddPiece(custom))''')
p.write_text(s)
