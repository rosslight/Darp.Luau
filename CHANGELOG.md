# Changelog

## [1.0.0](https://github.com/rosslight/Darp.Luau/compare/v0.3.0...v1.0.0) (2026-10-09)


### ⚠ BREAKING CHANGES

* give userdata types a static side and require their name ([#61](https://github.com/rosslight/Darp.Luau/issues/61))
* describe a userdata type once and make its methods values ([#59](https://github.com/rosslight/Darp.Luau/issues/59))
* rename CreateFunctionBuilder to CreateFunctionManual ([#58](https://github.com/rosslight/Darp.Luau/issues/58))

### Features

* coroutines as host values, async managed callbacks and async invocation ([#32](https://github.com/rosslight/Darp.Luau/issues/32)) ([b08cb6b](https://github.com/rosslight/Darp.Luau/commit/b08cb6b55d7976b869b24bae3f35e51883841c6d))
* declare userdata metamethods with [LuauMetamethod] ([#60](https://github.com/rosslight/Darp.Luau/issues/60)) ([3c6c8dd](https://github.com/rosslight/Darp.Luau/commit/3c6c8dde9350344f342867d8797c529f15464bb0))
* describe a userdata type once and make its methods values ([#59](https://github.com/rosslight/Darp.Luau/issues/59)) ([728b48a](https://github.com/rosslight/Darp.Luau/commit/728b48a2336786761bb1cc572ff6d8a7850e5af5))
* give userdata types a static side and require their name ([#61](https://github.com/rosslight/Darp.Luau/issues/61)) ([8758919](https://github.com/rosslight/Darp.Luau/commit/87589191325b2e0b99634b49444d6068ba5045b1))
* let CreateFunction delegates return tasks ([#46](https://github.com/rosslight/Darp.Luau/issues/46)) ([a05d196](https://github.com/rosslight/Darp.Luau/commit/a05d196e0801f2e15aa4e190036a117a1745bf68))
* let generated module functions and userdata methods return tasks ([#44](https://github.com/rosslight/Darp.Luau/issues/44)) ([0c1093d](https://github.com/rosslight/Darp.Luau/commit/0c1093d93da3fe5564c24cfe44359da23ed41b09))
* let userdata methods await ([#43](https://github.com/rosslight/Darp.Luau/issues/43)) ([6bf627c](https://github.com/rosslight/Darp.Luau/commit/6bf627c0431a4c4d3c522bc9afe93e73d02112b5))
* only read a Luau number as a type that can hold it ([#55](https://github.com/rosslight/Darp.Luau/issues/55)) ([9507d92](https://github.com/rosslight/Darp.Luau/commit/9507d92c7c4a2f2bc52af7e773fc79afbddd9514))
* rename CreateFunctionBuilder to CreateFunctionManual ([#58](https://github.com/rosslight/Darp.Luau/issues/58)) ([5e8ac44](https://github.com/rosslight/Darp.Luau/commit/5e8ac44c4c60ba4b761f1b5dec6f2f578ed885ba))
* stop a running script through its cancellation token ([#57](https://github.com/rosslight/Darp.Luau/issues/57)) ([7bdca00](https://github.com/rosslight/Darp.Luau/commit/7bdca00083cdff0d0ef4f800532cdd7038767edd))


### Bug Fixes

* harden callbacks, table access and disposal against scripts ([#50](https://github.com/rosslight/Darp.Luau/issues/50)) ([6335986](https://github.com/rosslight/Darp.Luau/commit/63359861675e5b91f6c0beb89ae0f9e65f674975))
* never start async callback work that cannot be awaited ([#49](https://github.com/rosslight/Darp.Luau/issues/49)) ([4325e0b](https://github.com/rosslight/Darp.Luau/commit/4325e0ba26b438e89c46425fb97e187c302807e0))
* release managed callback handles when Luau collects the function ([#56](https://github.com/rosslight/Darp.Luau/issues/56)) ([97eea1e](https://github.com/rosslight/Darp.Luau/commit/97eea1e509238a174b6883218b2b517946f99555))
* report export and callback shapes the generator cannot handle ([#53](https://github.com/rosslight/Darp.Luau/issues/53)) ([69fb9a1](https://github.com/rosslight/Darp.Luau/commit/69fb9a14312d3a9ecd70d0aac935e25d2fd5eb85))
* serialize releases and allow publishing existing tags ([#38](https://github.com/rosslight/Darp.Luau/issues/38)) ([5e0e910](https://github.com/rosslight/Darp.Luau/commit/5e0e910fb29ff5f86e89c3b4edea44eb56455456))


### Performance Improvements

* avoid allocations in async invocation and coroutine resumes ([#42](https://github.com/rosslight/Darp.Luau/issues/42)) ([2b1a768](https://github.com/rosslight/Darp.Luau/commit/2b1a7686038c78883565bae5974a4a3d0fef60a7))
* await generated async callbacks without a second state machine ([#48](https://github.com/rosslight/Darp.Luau/issues/48)) ([7f9c7c3](https://github.com/rosslight/Darp.Luau/commit/7f9c7c36dc78e205bed36d18ab22ff367d600c0c))

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
