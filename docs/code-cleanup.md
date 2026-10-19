# Kód- és függőségellenőrzés — 2026-10-19

A vizsgálat a megoldás mind a 12 projektjét, a fordításkor feloldott
projekt- és csomaghivatkozásokat, valamint a C#-szimbólumok hivatkozásait fedte le.
A reflexióval felismert szabálysémák, JSON-szerződések, interfészek megvalósításai,
belépési pontok, mentésmigrációk és parancssorból indítható diagnosztikák külön
ellenőrzést igényelnek: közvetlen C#-hívás nélkül is használatban lehetnek.

## Eltávolított részek

- A teljes `OpenTK` gyűjtőcsomag helyett a renderer a grafikai és matematikai,
  a játék emellett az ablakkezelő csomagot kéri. Az `OpenTK.Audio.OpenAL`,
  `OpenTK.Compute` és `OpenTK.Input` már nem szerepel a feloldott függőségekben.
- A tesztprojekt nem használt `coverlet.collector` csomagja kikerült; a CI és a
  dokumentált tesztparancsok nem indítottak ilyen lefedettségmérést.
- A Models projekt használaton kívüli közvetlen Engine-hivatkozása és globális
  importja megszűnt. Mind a 12 projektnek van fogyasztója, ezért projektet nem töröltünk.
- A fel nem használt Dendro `Mesh` exportáló réteg kikerült. A futó generátor,
  csontvázadapter, saját meshkészítés, forrásmegjelölés és licenc megmaradt.
- A régi víz- és tönkszabálygyár, a már nem hívott gráfgyár-adapterek,
  geometriai segédfüggvények és szinkron burkolók kikerültek.
- A használt növekedési, vitalitási és forgalmi sablon-API most az Editor
  projektjéből olvas; külön C#-os alapképletet nem tart fenn.
- Az aktív szerkesztőből el nem érhető korábbi műhely- és rendszertérkép-host,
  valamint az ezekhez tartozó állapot és hatástalan képernyőkapcsolók kikerültek.
  A rendszermodell számítása és parancssori exportja megmaradt.
- A régi `NU1701` elnyomások megszűntek. A szabálykatalógus és a generált
  lefedettségi dokumentum a tényleges Editor-definíciókra hivatkozik.
- Az algoritmusfordító külső MSBuild-futása egyfolyamatú, folyamat-újrahasznosítás
  nélkül: a fordítás után nem maradhatnak a befogott kimenetet nyitva tartó munkások.

## Útépítés és a szomszédos fák

Az útépítés korábban az egész erdőblokkot elrejtette, amíg a háttérben újraépült
a geometriája. A gyorsítótár most csempénként tartja nyilván a rajzolási tartományokat:
az út saját fái, holtfája, tönkjei és talajárnyalása azonnal elrejthetők, miközben
a szomszédos fák tovább rajzolódnak. A megjelenített tartalék részletességi szint
ugyanezt a szűrést kapja. A már elrejtett csempe régi fái útbontáskor sem térnek
vissza a függő geometriából.

A `--forest-smoke-test` azonos renderblokkban lévő fák képpontjait hasonlítja
össze útépítés előtt és a háttérben folyó átépítés közben.

## Ellenőrzés

A végleges Release build 0 figyelmeztetéssel és 0 hibával készült el.
A teljes tesztcsomagban **1983/1983 teszt sikeres**; a külső algoritmusfordítás,
a lefordított motor indítása és a világállapot átadása is végigfutott.
Az erdőrenderelési próba, az útépítési képpont-összehasonlítás és az editor
grafikus, illetve önálló ablakos próbája sikeres. A rendszermodell exportja
97 mennyiséget, 84 folyamatot és 17 fő visszacsatolási kört adott.

```powershell
dotnet restore ForesTycoon.sln --ignore-failed-sources -m:1 -p:NuGetAudit=false
dotnet build ForesTycoon.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test ForesTycoon.Tests/ForesTycoon.Tests.csproj -c Release --no-build
dotnet ForesTycoon/bin/Release/net8.0/ForesTycoon.dll --forest-smoke-test
dotnet ForesTycoon.Editor/bin/Release/net8.0/ForesTycoon.Editor.dll --smoke-test
python tools/update-rule-audit.py --check
```

A csomagverziók nem változtak; az ellenőrzés a helyben elérhető csomagokat használta.
