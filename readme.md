SQL → LINQ fordító

A projekt célja

A projekt célja egy olyan fordító (transpiler) készítése, amely SQL lekérdezéseket képes automatikusan C# LINQ kifejezésekké alakítani. A fejlesztés elsődleges célja a megvalósíthatóság vizsgálata, a lehetséges megoldások feltérképezése, valamint egy működő prototípus elkészítése.


Főbb célkitűzések

- SQL lekérdezések elemzése és feldolgozása.
- SQL absztrakt szintaktikai reprezentációjának (AST) előállítása.
- Az AST LINQ kifejezésekké történő transzformálása.
- A generált C# kód szintaktikai helyességének biztosítása.
- A megoldás korlátainak és alkalmazhatóságának vizsgálata.


Vizsgálandó kérdések

- Milyen SQL nyelvi elemek támogatása valósítható meg?
- Mely SQL konstrukciók fordíthatók egyértelműen LINQ-ra?
- Mely esetekben nem lehetséges vagy csak korlátozott a fordítás?
- Hogyan kezelhetők a különböző SQL dialektusok közötti eltérések?
- Milyen architektúra alkalmazható egy jól bővíthető fordító megvalósításához?


Tervezett technológiák

- C#
- .NET
- ANTLR 4
- Roslyn Compiler Platform
- LINQ


Tervezett fejlesztési lépések

- A releváns szakirodalom és meglévő megoldások feldolgozása.
- SQL nyelvtan kiválasztása és ANTLR parser elkészítése.
- Az AST feldolgozásának megvalósítása.
- LINQ kódgenerálás Roslyn segítségével.
- Tesztesetek készítése és a fordítás helyességének ellenőrzése.
- A megoldás korlátainak dokumentálása és értékelése.


Munkaidő nyílvántartás

https://docs.google.com/spreadsheets/d/1PGyCA7rX0jJ6ZRKQ_9c8zLb1XFkHqzliBGZACF3ObDk/edit?usp=sharing
