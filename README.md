# SysProg-MuseumApi-2

Zadatak 19:
Kreirati Web server koji klijentu omogućava pretragu umetničkih dela korišćenjem Metropolitan Museum of Art Collection API-a. Pretraga se može vršiti pomoću filtera koji se definišu u okviru query-a. Spisak umetničkih dela koje zadovoljavaju uslov se vraćaju kao odgovor klijentu. Svi zahtevi serveru se šalju preko browser-a korišćenjem GET metode. Ukoliko navedena umetnička dela ne postoje, prikazati grešku klijentu.

Način funkcionisanja Metropolitan Museum of Art Collection API-a je moguće proučiti na sledećem linku:
https://metmuseum.github.io/

Primer poziva serveru:
https://collectionapi.metmuseum.org/public/collection/v1/search?q=sunflowers
