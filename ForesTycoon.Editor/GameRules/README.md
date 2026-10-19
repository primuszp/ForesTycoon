# Az editor által létrehozott játékszabályok

`default.project.json` a játék kiinduló szabályprojektje, nem a motorból előállított
szabálylista. Az Editorban közvetlenül megnyitható:

```powershell
dotnet run --project ForesTycoon.Editor -- --project ForesTycoon.Editor/GameRules/default.project.json
```

A Rules projekt erőforrásként csomagolja, ezért a játék és a grafika nélküli
szimuláció az Editor telepítése nélkül is ugyanazt a dokumentumot futtatja.
Az új játék alapjának módosításához a fájlt szerkeszd, majd fordítsd újra a játékot.
Futó játéknak az editor szokásos projektalkalmazásával adható át egy szerkesztett projekt.

A 4-es projektverzió szabálykönyve teljes definíció: a törléshez nincs szükség
`removedStarters` bejegyzésre. Hiányzó számítási kötésből inaktív, korlátozott nulla
lesz; a motor nem állítja vissza az eredeti összefüggést. Egyes fizikai mennyiségek
pozitív alsó korlátja továbbra is érvényes. Az 1–3-as projektek első normalizálásakor
egyszeri migráció történik 4-es verzióra; a meglévő szerkesztett gráfok megmaradnak.

A `gameplay` gráfok az erdő kezdeti eloszlását, telepítést, kitermelési engedélyt,
gazdasági döntéseket, küldetésfeltételeket és vadak döntéseit számolják. Ezek tiszta
gráfok: mért bemeneteket kapnak, nincs beépített eredményre visszaesés.
A motor a geometriát, végrehajtást, készletmegőrzést és mentést végzi.
A hangolás mind az 51 alapértéke a projekt része; a játék a teljes értékkészletet
exportálja és menti, ezért a kódbeli alapértékek későbbi változása sem írja át a projektet.

A vadak életkora, éhsége, legelése, vándorlási igénye, a legelő regenerációja és
a mozgási algoritmus meghívása a `wildlife` időzítésű objektumszabályokban van.
A processzor és forwarder következő munkafázisát a `machine.workflow`, a teherautóét
a `truck.workflow` gráf választja. A korábbi részgráfok eredményei beköthetők ezekbe;
az elsőbbség, feltétel és küszöb a dokumentumban módosítható.

A `control.allow`, `control.home`, `control.upkeep`, `supply.active` és
`loading.depart` gráfok adják a várakozás, hazatérés, fenntartás, utánpótlás és
rakott indulás elsőbbségeit is. A `mission.state` dönt az egyidejű küldetésfeltételekről.
A `population` szabályok az automatikusan felismert állattípusok kívánt létszámát
állítják és kérik a példányosítást; a `birth` szabályok az új egyedeket inicializálják.
A típusok létrehozásához nincs külön fajonkénti gyár vagy editorlista.

A motor a geometriát, az útkeresést, a test mozgatását, a daru rönkciklusának
végrehajtását, a készletmegőrzést és a mentést szolgáltatja. Új fizikai művelet vagy
játékosparancs új motor-API-t igényelhet; az ezek közötti szimulációs logikát az
editorban kell létrehozni. A terep és a vizuális elemek generálása külön motorfeladat.

Az objektummodell az osztályokból és interfészekből épül fel. Az `Animal` leszármazottja
külön editorregisztráció és `RuleView` nélkül megjelenik; nyilvános numerikus, logikai
és enum tulajdonságai az örökölt mezőkkel együtt beköthetők. A `DisplayName` adja a
szép nevet, a `RuleField` a mértékegységet és korlátokat, a `RuleRelation` és
`RuleCollection` az objektumkapcsolatokat. Részletek: [objektummodell](../../docs/rule-editor.md).
