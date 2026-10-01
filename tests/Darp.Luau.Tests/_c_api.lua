-- Exploratory editor/API annotations, not an executable test fixture.
-- See docs/AdvertisementAnalyzerExperiment.md for scope and unresolved API choices.

---@class FieldDef
---@field key string
---@field title string
---@field display any

---@class ProtocolField
local ProtocolField = {}

---@param opts FieldDef
---@return ProtocolField
function ProtocolField:_ctor(opts) end

---@class Field
---@field u8  fun(opts: FieldDef): ProtocolField
---@field u16 fun(opts: FieldDef): ProtocolField
---@field u24 fun(opts: FieldDef): ProtocolField
---@field u32 fun(opts: FieldDef): ProtocolField
---@field u48 fun(opts: FieldDef): ProtocolField
---@field u64 fun(opts: FieldDef): ProtocolField
---@field u128 fun(opts: FieldDef): ProtocolField
---@field s8  fun(opts: FieldDef): ProtocolField
---@field s16 fun(opts: FieldDef): ProtocolField
---@field s24 fun(opts: FieldDef): ProtocolField
---@field s32 fun(opts: FieldDef): ProtocolField
---@field s48 fun(opts: FieldDef): ProtocolField
---@field s64 fun(opts: FieldDef): ProtocolField
---@field s128 fun(opts: FieldDef): ProtocolField

---@type Field
Field = Field

-- Dissector arguments
---@class Tvb
local Tvb = {}

---@class TreeItem
local TreeItem = {}
-- TreeItem is where you add fields etc. Wireshark has several add methods; one important one:
-- tree_item:add_packet_field(proto_field [,tvbrange], encoding, ...)
function TreeItem:add_packet_field(...) end

---@class Proto
---@field fields ProtocolField[]
---@field dissector fun(tvb: Tvb, tree: TreeItem)
local Proto = {}

-- If your environment uses Protocol(), declare it. (Wireshark usually uses Proto()).
---@param name string
---@param description string
---@return Proto
function Protocol(name, description) end
