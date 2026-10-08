# Animált GLB modellek és erdei szarvasok

## Animációs runtime konszolidáció (2026-10-08)

A GLB-betöltés és a futásidejű mintavétel külön forrásban található:
`AnimatedGlbModel.cs`, illetve `AnimatedGlbModel.Animation.cs`. A típus és az assetadatok közösek;
a szétválasztás nem új formátum vagy kompatibilitási ág. Az `AnimationPath` és
`AnimationInterpolation` enum a szöveges GLB-értékek fordítása után egyértelmű runtime típust ad.

Nulla/egy crossfade súlynál csak az aktív klipet mintavételezzük; köztes súlynál a meglévő TRS-blend
marad. Az in-place gyökércsatornát egyszer, a kezdőidőnél olvassuk. Érvénytelen idő, blend vagy
gyökérindex kivételt ad, mielőtt nem véges transzformáció kerülhetne a GPU-ra. A póz tömbjeit
újrahasználjuk; a regressziós teszt ismételt crossfade-nél nulla szálankénti memóriafoglalást ellenőriz.

Az `AnimatedModelRenderer` először asset-sorrendben rajzolja a tömör/kivágott felületeket, utána
csak a BLEND primitíveket rendezi hátulról előre. Az árnyékmenet nem rendez. A sorrendet minden
hívás frissíti, ezért futásidejű anyagváltáskor is helyes. A rendezés továbbra is példányon belüli;
egymást metsző átlátszó példányokhoz jelenetszintű megoldás szükséges.


A felhasználó `realistic_animated_elk_3d_model.glb` fájlja változatlan másolatként bekerült az `Assets/Wildlife/elk.glb` állományba. Az eredeti Downloads-beli fájl érintetlen maradt.

## Használat

A Nézet menü **Erdei szarvasok** kapcsolója szabályozza a megjelenítést. A **Szarvas megkeresése** a kamerát az egyik élőhelyre irányítja és közelít. A kezelőfelületen látszik a kiválasztott élőhelyekből megjelenő szarvasok száma. A kapcsoló az eredeti színalapú módban is elérhető.

Az állatok az erdők tisztásain és szélein legelnek, időnként rövid, lassú sétát tesznek. A modell valódi `Stand_Eating_01` és `WalkSlow` klipjei játszódnak le, csontonként kevert átmenettel. Az állatok eltérő, seedelt fázisokat kapnak; a mozgás szimulációs időből származik, ezért szünetben megáll, betöltéskor pedig a világ idejéből reprodukálható. Ez jelenleg környezeti látványelem: nem fogyasztja az erdő biomasszáját, nincs vadászat vagy vadállomány-gazdálkodás.

Az élőhelyválasztás kizárja a vizet, utakat, közvetlen útszomszédságot és meredek csempéket. A tisztások előnyt kapnak a zárt lombkoronájú helyekkel szemben. A tényleges fatörzsek elhelyezése alapján a tágabb hézagot keresi; a modell a terep magasságához és dőléséhez igazodik. Erdőművelés, növekedés és terepváltozás után az élőhelyeket újraértékeli. Nem teljes ütközéses útkereső vagy lábankénti IK-rendszer.

## Motoroldali megvalósítás

- `AnimatedGlbModel`: GLB 2.0 csomag, node-hierarchia, TRS/mátrix-transzformációk, skin és inverz kötési mátrixok, csatornák/klippek betöltése. LINEAR, STEP és CUBICSPLINE mintavétel; kvaterniós rotáció és animációk közötti TRS-keverés. Az assetréteg nem hív OpenGL-t.
- `AnimatedModelRenderer`: statikus indexed mesh-pufferek, csúcspontonként négy csontbefolyás és GPU skinning. Legfeljebb 64 csont skinenként, a mátrixok külön uniform bufferben. Az ugyanahhoz a skinhez tartozó egymást követő mesh-ek között nem tölti újra ugyanazt a csontpalettát. A merev, csont alá csatolt agancs node-transzformációja is animálódik.
- `PngImage`: a GLB-be csomagolt 8 bites, nem interlace-elt RGB/RGBA PNG textúrák dekódolása külső csomag vagy platformfüggő képkezelő nélkül.
- Diffúz textúrák és anyagszín, napfény, borultság/vihar/villám megvilágítás, vetett és fogadott árnyék. A textúra és megvilágítás a grafikai kapcsolókat követi. Az eredeti módban egyszerű színalapú megjelenítés marad.
- `WildlifeRenderer`: legfeljebb 16 környezeti állat; egy közös asset és GPU-pufferkészlet, állatonként újrahasznosított pose-tömbök. A fő renderelés kamerán kívüli állatainál kimarad az animáció mintavétele. Az árnyékmenet a szükséges árnyékvetőket is rajzolja.

A csontmátrix a kötési helyzetből az animált közös koordinátatérbe visz, utána kerül rá az állat világtranszformációja. A csontok sorrendjét a skin joint-listája határozza meg, nem a node-indexek sorrendje. A megvalósítás szakmai alapja a [Khronos glTF skinning leírása](https://github.khronos.org/glTF-Tutorials/gltfTutorial/gltfTutorial_020_Skins.html) és az [animációk leírása](https://github.khronos.org/glTF-Tutorials/gltfTutorial/gltfTutorial_007_Animations.html).

A betöltő korlátozott GLB-részhalmazt kezel: indexed háromszögek, POSITION/NORMAL/TEXCOORD_0, JOINTS_0/WEIGHTS_0, beágyazott PNG; a jelen modell specular-glossiness kiterjesztésének diffúz részét és az alap metallic-roughness anyag diffúz részét olvassa. Nincs teljes PBR, normal-map feldolgozás, morph target, Draco/KTX tömörítés, sparse accessor, külső URI vagy JPEG. Az ezekre épülő modellekhez külön bővítés szükséges; az elk modell működéséhez ezek nem kellenek.

## Ellenőrzés

135 automatizált teszt sikeres. Az új teszt a 38 csontos modellt, a beágyazott textúrát, a legelési deformációt, az animációhurok reprodukálhatóságát, a keverést és a helyes modellméretet vizsgálja.

`--wildlife-smoke-test`: GPU animáció két különböző időpontban, azonos szüneteltetett képkocka, legelés/séta, textúra nélküli és eredeti mód, erdei elhelyezés/árnyék, élőhely eltávolítása. Képek az `artifacts/wildlife` mappában. A grafikai/időjárási smoke és a teljes játék 120 képkockás futási tesztje is sikeres.

```powershell
dotnet artifacts/engine-validation/ForesTycoon.dll --wildlife-smoke-test
```

A legutolsó, ellenőrzött build az `artifacts/engine-validation/run-game.ps1` indítóval futtatható. A normál Visual Studio build is ugyanazt a forrást és assetet használja.

A szarvasok sötét sziluettkontúrja a Nézet → Szarvaskontúrok kapcsolóval vezérelhető. A normálirányú kitolás a csontozott animáció után történik, így az agancs és a test mozgását is követi. A vonal vastagsága körülbelül 0,7 képpont; távoli nézetben és alacsony grafikai minőségnél kikapcsol, közepes minőségnél legfeljebb négy, magasnál nyolc látható állat kap kontúrt. Az eredeti színes mód és az árnyéktérkép nem használ kontúrt. A grafikai próba a kapcsolás képi hatását és a visszakapcsolás pontos eredményét is ellenőrzi.
