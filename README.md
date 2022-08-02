# Fork 

This fork's main purpose is to implement experimental sharing of a single UDP Socket to allow for "relatively high performance" querying of thousands of servers at once without spawning thousands of sockets and their overhead. ASync and other fixes are in the original library, which is https://github.com/Florian2406/Okolni-Source-Query

Some words of wisdom if you want to query thousands of servers to collect statistics. Linux works best with its network stack, outperforming Windows greatly. Use The QueryPool like in the example of this doc/example repo. Swallow timeout exceptions and such for failed servers, there will always be some. It's even on Linux, you should  limit to ~1000 or so concurrent queries, the "query pool" really isn't optimized and will likely fill the socket's buffer at some point and start missing packets if too busy. You can use the Steam Web API to query a list of ~20k servers for a specific APP ID. The Steam Master Servers are aggressively rate limited from what I've tested, and return only the IP/Port, whereas the Steam Web API returns almost the same information as A2S_Info/GetServerInfo.

Games like Arma 3 use A2S_Rules for their mods, but not in a compatiable format, watch out for them. Games running on Gold Source (https://en.wikipedia.org/wiki/GoldSrc) are not supported by this library.  It's not safe to assume that each server on the server list is an actual unique server. I found a few servers that use different Query Ports on the same IP, but return the same Port (Game Port), so make sure you are identifying servers by Addr/QueryPort and not Addr/Port. Some servers block A2S_Rules/A2S_Players, and some games also do not support them. Some games like Post Scriptum return 0 for player count in A2S_Players and only return correct player count in A2S_Rules, and some games like Rust can have too many players. A2S_Players returns 'players', the amount of players on, as a byte, so a max of 255. Games treat this overflow differently, some return 255 if over 255 are on, Rust for example will just return the player count mod 255 (i.e 550 players on, will show 40). For those games, specifically Rust, you can get the full list of players from A2S_Players. This library supports over 255 players, but beware the underlying A2S_Players response also has a player count which is a byte, so some libraries will only parse a max of 255 players even if there is more.

** Work in Progress **

* Add pooling/batching, use a single UDP Socket to send and receive from many servers at once. On Windows, 512 queries at once seems to be stable. On Linux, 1024 is stable, but I haven't tried to push it past that.

* Move to .NET 6 

* Rework implementation of retries to work with Send/Recieve timeouts

* Dropped support for The Ship and all related properties

*** Note on Retries and Send/Recieve Timeouts ***

Keep in mind the potentially worst case for execution time timeout*retries*4 (1 send auth, 1 receive auth, 1 send challenge, 1 receive result).

For example, if you have send/recv timeout set to 500ms, and timeout as default (10 times), and the server is dead/won't respond, it will take 5000ms to detect that, as the library will send 10 challenge packets which will timeout after 500ms.


Todo:

[ ] Proper Unit Tests

[ ] Further Testing

[ ] Code clean up





# Okolni-SourceEngine-Ouery
<!--
*** Thanks for checking out the Best-README-Template. If you have a suggestion
*** that would make this better, please fork the repo and create a pull request
*** or simply open an issue with the tag "enhancement".
*** Thanks again! Now go create something AMAZING! :D
-->



<!-- PROJECT SHIELDS -->
<!--
*** I'm using markdown "reference style" links for readability.
*** Reference links are enclosed in brackets [ ] instead of parentheses ( ).
*** See the bottom of this document for the declaration of the reference variables
*** for contributors-url, forks-url, etc. This is an optional, concise syntax you may use.
*** https://www.markdownguide.org/basic-syntax/#reference-style-links
-->
[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![LGPL License][license-shield]][license-url]
[![Nuget][nuget-shield]][nuget-url]
<!-- [![LinkedIn][linkedin-shield]][linkedin-url] -->



<p align="center">
<br />
<a href="https://github.com/Florian2406/Okolni-Source-Query/blob/master/doc/Okolni.Source.Example/Program.cs">View Demo</a>
·
<a href="https://github.com/Florian2406/Okolni-Source-Ouery/issues">Report Bug</a>
·
<a href="https://github.com/Florian2406/Okolni-Source-Ouery/issues">Request Feature</a>
</p>



<!-- TABLE OF CONTENTS -->
<details open="open">
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgements">Acknowledgements</a></li>
  </ol>
</details>



<!-- ABOUT THE PROJECT -->
## About The Project

The project is a simple package made for C#/.NET Source Query Requests. Main features are the default requests that can also be found on the source query documentation in the [Valve developer community wiki](https://developer.valvesoftware.com/wiki/Server_queries). For a list of implemented requests see the roadmap below.

### Built With

* [.NET-Standard 2.0](https://docs.microsoft.com/de-de/dotnet/standard/net-standard)
* [.NET 5](https://dotnet.microsoft.com/download/dotnet/5.0)

## Usage

First download the [nuget package](https://www.nuget.org/packages/Okolni.Source.Query/) and import the namespace `Okolni.Source.Query` which is the main namespace. After that you can use the project as in the code example below. You create a query connection with an ip and a port and after connecting you can get started with your requests.
```
IQueryConnection conn = new QueryConnection();

conn.Host = "127.0.0.1"; // IP
conn.Port = 27015; // Port

conn.Connect(); // Create the initial connection

var info = conn.GetInfo(); // Get the Server info
var players = conn.GetPlayers(); // Get the Player info
var rules = conn.GetRules(); // Get the Rules
```
_For an example view the demo project [Demo](https://github.com/Florian2406/Okolni-Source-Query/blob/master/doc/Okolni.Source.Example/Program.cs)_

<!-- _For more examples, please refer to the [Documentation](https://example.com)_ -->


## Roadmap

Implemented so far:
- Source Query Servers
- Info Request
- Players Request
- Rules Request
- The Ship Servers

Missing at the moment:
- Multipacket responses
- Goldsource Servers

Also see the [open issues](https://github.com/Florian2406/Okolni-Source-Ouery/issues) for a list of proposed features (and known issues).


## Contributing

Contributions are what make the open source community such an amazing place to be learn, inspire, and create. Any contributions you make are **greatly appreciated**.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request


## License

Distributed under the LGPL-3.0 License. See `LICENSE` for more information.


## Contact

Florian Adler - [@florian2406](https://twitter.com/florian2406)

Project Link: [https://github.com/Florian2406/Okolni-Source-Ouery](https://github.com/Florian2406/Okolni-Source-Ouery)



<!-- ACKNOWLEDGEMENTS -->
## Acknowledgements
* [GitHub Emoji Cheat Sheet](https://www.webpagefx.com/tools/emoji-cheat-sheet)
* [Img Shields](https://shields.io)
* [Choose an Open Source License](https://choosealicense.com)
* [Valve developer community](https://developer.valvesoftware.com/wiki/Server_queries)





<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://www.markdownguide.org/basic-syntax/#reference-style-links -->
[contributors-shield]: https://img.shields.io/github/contributors/florian2406/Okolni-Source-Query?style=for-the-badge
[contributors-url]: https://github.com/florian2406/Okolni-Source-Query/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/florian2406/Okolni-Source-Query?style=for-the-badge
[forks-url]: https://github.com/florian2406/Okolni-Source-Query/network/members
[stars-shield]: https://img.shields.io/github/stars/florian2406/Okolni-Source-Query?style=for-the-badge
[stars-url]: https://github.com/florian2406/Okolni-Source-Query/stargazers
[issues-shield]: https://img.shields.io/github/issues/florian2406/Okolni-Source-Query?style=for-the-badge
[issues-url]: https://github.com/florian2406/Okolni-Source-Query/issues
[license-shield]: https://img.shields.io/github/license/florian2406/Okolni-Source-Query?style=for-the-badge
[license-url]: https://github.com/florian2406/Okolni-Source-Query/blob/master/LICENSE
[nuget-shield]: https://img.shields.io/nuget/dt/Okolni.Source.Query?style=for-the-badge
[nuget-url]: https://www.nuget.org/packages/Okolni.Source.Query
