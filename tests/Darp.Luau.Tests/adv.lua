-- Exploratory API sketch, not an executable test fixture.
-- See docs/AdvertisementAnalyzerExperiment.md for the current smoke-test contract.

local field11 = aa.Field.u8{key="p.u8", title="u8", display=base.DEC} -- support u8, u16, u24, u32, u48, u64, u128
local field12 = aa.Field.s32{key="p.s24", title="s24", display=base.DEC} -- support s8, s16, s24, s32, s48, s64, s128
local field13 = aa.Field.f16{key="p.f16", title="f16", display=base.DEC} -- support f16, f32, f64
local msg_types = { [0] = "Unknown", [1] = "Hello", [2] = "Data", [3] = "Bye" }
local field14 = aa.Field.u8{key="p.enum", title="Enum", display=base.DEC, enum=msg_types} -- available on u*, s*
local field15 = aa.Field.bool{key="p.bool", title="Bool", display=base.DEC}

-- Other
local field21 = aa.Field.bytes{key="p.bytes", title="Raw Bytes"}
local field21 = aa.Field.sbytes{key="p.sbytes", title="Raw signed Bytes"}
local field22 = aa.Field.string{key="p.string", title="My String"}

local field31 = aa.Field.absolute_time{key="p.abs_time", title="Timestamp"}
local field32 = aa.Field.relative_time{key="p.rel_time", title="Timestamp"}
local field33 = aa.Field.guid{key="p.guid", title="Guid"}
local field34 = aa.Field.protocol{key="p.guid", title="Guid"}

-- Init protocol
local p = aa.Protocol("bitproto", "Bit-Packed Proto")
p.fields = { field11, field12, field13, field14, ... }

function p.dissector(evt, tree)
  local existing1_data = evt:get("some.other.field")
  local existing2_data = evt:get("some.other.field")
  if not existing1_data or not existing2_data then return end

  if not existing1_data or existing1_data.value ~= 123 then return end
  -- local total_range = evt.range
  -- local total_data = evt.raw = evt.value

  local range = existing2_data.range
  local json = existing2_data:json()
  local protobuf = existing2_data:protobuf()

  local my_tree = tree:add("My tree", range)
  -- Add a slice
  my_tree.add_le(field13, range:slice(0,2))
  -- Add a value
  my_tree.add(field11, existing1_data.value)
  -- Add a json value (if valid, includes the range)
  local json_value = json:get("path"):get("to"):get("value"):get_integer("path.to.value")
  if json_value then
    my_tree.add(field12, json_value)
  end
  -- Add a protobuf value (if valid, includes the range)
  local protobuf_value = protobuf:get_varint(4)
  if protobuf_value then
    my_tree.add(field32, protobuf_value)
  end
end

aa.register_dissector(p)
