# Changelog

## [0.3.0](https://github.com/rosslight/Darp.Luau/compare/v0.2.0...v0.3.0) (2026-10-06)


### Features

* add library generator ([#19](https://github.com/rosslight/Darp.Luau/issues/19)) ([c4af33b](https://github.com/rosslight/Darp.Luau/commit/c4af33b77267cc66e9ed673abe86ea0b15866cba))
* add userdata generator ([#21](https://github.com/rosslight/Darp.Luau/issues/21)) ([533d855](https://github.com/rosslight/Darp.Luau/commit/533d855b8e738bc015424e00fd7cdaea6dc5c439))
* Move callbacks into native code ([#23](https://github.com/rosslight/Darp.Luau/issues/23)) ([3738a80](https://github.com/rosslight/Darp.Luau/commit/3738a80f197fec2f020932fd9eba55bc650a7758))

## [0.2.0](https://github.com/rosslight/darp-luau/compare/v0.1.1...v0.2.0) (2026-03-23)


### Features

* add chunks api ([#17](https://github.com/rosslight/darp-luau/issues/17)) ([add438b](https://github.com/rosslight/darp-luau/commit/add438bd8697106d697a7daf3dfb3b538041305b))
* add multi return DoString calls ([#15](https://github.com/rosslight/darp-luau/issues/15)) ([0ef8a5a](https://github.com/rosslight/darp-luau/commit/0ef8a5a930e6e185dcfea9e01353efd601ae7d20))
* Add support for require statement within script ([#7](https://github.com/rosslight/darp-luau/issues/7)) ([c901dae](https://github.com/rosslight/darp-luau/commit/c901daeea70e4a1ba7e8a74ff0210402e750f46e))
* support managed userdata in function callbacks ([#18](https://github.com/rosslight/darp-luau/issues/18)) ([0e960d3](https://github.com/rosslight/darp-luau/commit/0e960d31e92bfa64009d5d3ad42c4ba74f1659d5))
* support multiple values in function returns ([#13](https://github.com/rosslight/darp-luau/issues/13)) ([871440d](https://github.com/rosslight/darp-luau/commit/871440d70395dcb115d916373895a649a28a30d1))

## [0.1.1](https://github.com/rosslight/darp-luau/compare/v0.1.0...v0.1.1) (2026-03-16)


### Bug Fixes

* Publish Generator as part of the main package ([a887f7f](https://github.com/rosslight/darp-luau/commit/a887f7fce18ae494fc74135bb6fb62bb66e2922d))

## [0.1.0](https://github.com/rosslight/darp-luau/compare/v0.0.1...v0.1.0) (2026-03-16)


### Features

* Add compile time conversion checks using an intermediary IntoLuau struct ([80a4e48](https://github.com/rosslight/darp-luau/commit/80a4e48d69ea268097614f1bb3d30f5e36772dbc))
* Add enumeration capabilities to lua tables ([eee95bf](https://github.com/rosslight/darp-luau/commit/eee95bf8671935b87a0bd85c946ce5408ba4f318))
* Add first version of the CreateMethod interceptor generator ([61801d4](https://github.com/rosslight/darp-luau/commit/61801d493a49736303514e08179f1632fe233f6b))
* add libraries configuration ([#6](https://github.com/rosslight/darp-luau/issues/6)) ([3c6763e](https://github.com/rosslight/darp-luau/commit/3c6763e6e7b8eebc747ff711462d9d1eba2f7c38))
* add proper documentation ([#10](https://github.com/rosslight/darp-luau/issues/10)) ([c3cfdac](https://github.com/rosslight/darp-luau/commit/c3cfdaccde07252207ed56a4189efe65f334e336))
* add userdata ([#3](https://github.com/rosslight/darp-luau/issues/3)) ([268953b](https://github.com/rosslight/darp-luau/commit/268953b1aed5f87c64810d389400f6f8d576bc7d))
* Basic support for lua functions ([bdffa86](https://github.com/rosslight/darp-luau/commit/bdffa86589de497153e03ee79d38b81237b8fa76))
* Improve diagnostics ([3138957](https://github.com/rosslight/darp-luau/commit/31389579a95bfc32e5cb6cefd9f87d4badd12d1d))
* Support a different number of arguments ([4c2ddc8](https://github.com/rosslight/darp-luau/commit/4c2ddc808b38f3c776d8dfe5eba7a684da76fff7))
* Support a return value in functions ([84f7265](https://github.com/rosslight/darp-luau/commit/84f7265cf0384970c42f88732f3ca934057b56dd))
* Support buffer ([#1](https://github.com/rosslight/darp-luau/issues/1)) ([b26c36d](https://github.com/rosslight/darp-luau/commit/b26c36d7cfe02c9981a77c7265b2ba5a7862bb57))
* Support different types of parameters ([35abdb2](https://github.com/rosslight/darp-luau/commit/35abdb27394ba1c112d2ca49bf8507b333792bd1))
* Support enums as numbers ([6dbac70](https://github.com/rosslight/darp-luau/commit/6dbac704f7d7c30f2c2405d689c57614f0546839))


### Bug Fixes

* A number of stack imbalance bugs ([e5ffdc2](https://github.com/rosslight/darp-luau/commit/e5ffdc2970e6da82634f4e599a8d0c573d16726a))
* Add proper caching for userdata ([61b9630](https://github.com/rosslight/darp-luau/commit/61b9630bc80f9df345eee1a58d71eb8c72d8a8e9))
* Properly support nil values ([916c96e](https://github.com/rosslight/darp-luau/commit/916c96e5e4ec5d2a37374c31640d03e6ff241e80))
* Use Darp.Luau.Native for working shared libraries ([562111d](https://github.com/rosslight/darp-luau/commit/562111ddf1ef57e346f6843fcee89cf6a4b4b37a))
