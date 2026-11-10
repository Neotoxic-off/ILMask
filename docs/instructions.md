# IL instruction mapping

How each IL instruction becomes mask bytes in `IlMask.TryFromIl` and `IlMask.TryFromListing`.
`??` is a wildcard: it matches any byte, and `IlMaskMatch.Wildcards` returns the bytes under it.

## Rule

An instruction is its opcode bytes, then its operand bytes (little-endian, as ECMA-335 encodes them).

- The **opcode** is always exact. Two-byte opcodes start with `FE`.
- The **operand** is exact, **except metadata tokens**, which are always `?? ?? ?? ??`: their values
  change from one build to another, and a listing does not show them.

## Operand types

| Operand type          | Size                    | In the mask | Encoded as                                                       | Example                     | Mask                                     |
|-----------------------|-------------------------|-------------|------------------------------------------------------------------|-----------------------------|------------------------------------------|
| `InlineNone`          | 0                       | -           | nothing                                                          | `ret`                       | `2A`                                     |
| `ShortInlineI`        | 1                       | exact       | `int8` / `uint8`                                                 | `ldc.i4.s 42`               | `1F 2A`                                  |
| `InlineI`             | 4                       | exact       | `int32`                                                          | `ldc.i4 1000`               | `20 E8 03 00 00`                         |
| `InlineI8`            | 8                       | exact       | `int64`                                                          | `ldc.i8 1000`               | `21 E8 03 00 00 00 00 00 00`             |
| `ShortInlineR`        | 4                       | exact       | `float32` bits                                                   | `ldc.r4 1.5`                | `22 00 00 C0 3F`                         |
| `InlineR`             | 8                       | exact       | `float64` bits                                                   | `ldc.r8 1.5`                | `23 00 00 00 00 00 00 F8 3F`             |
| `ShortInlineVar`      | 1                       | exact       | `uint8` argument or local index                                  | `ldloc.s 4`                 | `11 04`                                  |
| `InlineVar`           | 2                       | exact       | `uint16` argument or local index                                 | `ldloc 4`                   | `FE 0C 04 00`                            |
| `ShortInlineBrTarget` | 1                       | exact       | `int8` target minus the offset of the next instruction           | `IL_0000: br.s IL_0000`     | `2B FE`                                  |
| `InlineBrTarget`      | 4                       | exact       | `int32` target minus the offset of the next instruction          | `IL_0000: br IL_0000`       | `38 FB FF FF FF`                         |
| `InlineSwitch`        | 4 + 4 × n               | exact       | `uint32` count n, then n `int32` targets minus the switch's end  | `switch (IL_0000, IL_0000)` | `45 02 00 00 00 F3 FF FF FF F3 FF FF FF` |
| `InlineField`         | 4                       | `??`        | field token                                                      | `ldfld int32 Foo::_value`   | `7B ?? ?? ?? ??`                         |
| `InlineMethod`        | 4                       | `??`        | method token                                                     | `call void Foo::Bar()`      | `28 ?? ?? ?? ??`                         |
| `InlineType`          | 4                       | `??`        | type token                                                       | `box Foo`                   | `8C ?? ?? ?? ??`                         |
| `InlineTok`           | 4                       | `??`        | field, method or type token                                      | `ldtoken Foo`               | `D0 ?? ?? ?? ??`                         |
| `InlineString`        | 4                       | `??`        | user string token                                                | `ldstr "hello"`             | `72 ?? ?? ?? ??`                         |
| `InlineSig`           | 4                       | `??`        | standalone signature token                                       | `calli int32()`             | `29 ?? ?? ?? ??`                         |

### Listing text (`TryFromListing`)

The listing must give `TryFromListing` the same bytes as the compiled method:

- **Constants**: decimal (`ldc.i4 -1000`), hex raw bits (`ldc.i4 0xfffffc18`), floats (`ldc.r8 1E-300`),
  or raw bytes for NaN and infinities (`ldc.r4 (00 00 C0 FF)`).
- **Branches and `switch`**: targets are `IL_xxxx` labels; the delta comes from the instruction's own label,
  so each line needs its real label.
- **Arguments and locals**: a number (`ldloc.s 4`), a generated name (`V_4` for locals, `A_4` for arguments),
  or a name declared in the pasted `.method` header or `.locals` (`ldarg.s index`). `instance` methods count
  `this` as argument 0. A name the text does not declare becomes `??` (1 or 2 bytes).
- **Tokens**: the text after the mnemonic (`int32 Foo::_value`) is ignored; the operand is `?? ?? ?? ??`.

## Every instruction

Generated by `IlMask.TryFromListing` on `IL_0000: <example>`. Branch examples jump back to `IL_0000`.

| Instruction | Opcode | Operand type | Example | Mask |
|-------------|--------|--------------|---------|------|
| `nop` | `00` | InlineNone | `nop` | `00` |
| `break` | `01` | InlineNone | `break` | `01` |
| `ldarg.0` | `02` | InlineNone | `ldarg.0` | `02` |
| `ldarg.1` | `03` | InlineNone | `ldarg.1` | `03` |
| `ldarg.2` | `04` | InlineNone | `ldarg.2` | `04` |
| `ldarg.3` | `05` | InlineNone | `ldarg.3` | `05` |
| `ldloc.0` | `06` | InlineNone | `ldloc.0` | `06` |
| `ldloc.1` | `07` | InlineNone | `ldloc.1` | `07` |
| `ldloc.2` | `08` | InlineNone | `ldloc.2` | `08` |
| `ldloc.3` | `09` | InlineNone | `ldloc.3` | `09` |
| `stloc.0` | `0A` | InlineNone | `stloc.0` | `0A` |
| `stloc.1` | `0B` | InlineNone | `stloc.1` | `0B` |
| `stloc.2` | `0C` | InlineNone | `stloc.2` | `0C` |
| `stloc.3` | `0D` | InlineNone | `stloc.3` | `0D` |
| `ldarg.s` | `0E` | ShortInlineVar | `ldarg.s 4` | `0E 04` |
| `ldarga.s` | `0F` | ShortInlineVar | `ldarga.s 4` | `0F 04` |
| `starg.s` | `10` | ShortInlineVar | `starg.s 4` | `10 04` |
| `ldloc.s` | `11` | ShortInlineVar | `ldloc.s 4` | `11 04` |
| `ldloca.s` | `12` | ShortInlineVar | `ldloca.s 4` | `12 04` |
| `stloc.s` | `13` | ShortInlineVar | `stloc.s 4` | `13 04` |
| `ldnull` | `14` | InlineNone | `ldnull` | `14` |
| `ldc.i4.m1` | `15` | InlineNone | `ldc.i4.m1` | `15` |
| `ldc.i4.0` | `16` | InlineNone | `ldc.i4.0` | `16` |
| `ldc.i4.1` | `17` | InlineNone | `ldc.i4.1` | `17` |
| `ldc.i4.2` | `18` | InlineNone | `ldc.i4.2` | `18` |
| `ldc.i4.3` | `19` | InlineNone | `ldc.i4.3` | `19` |
| `ldc.i4.4` | `1A` | InlineNone | `ldc.i4.4` | `1A` |
| `ldc.i4.5` | `1B` | InlineNone | `ldc.i4.5` | `1B` |
| `ldc.i4.6` | `1C` | InlineNone | `ldc.i4.6` | `1C` |
| `ldc.i4.7` | `1D` | InlineNone | `ldc.i4.7` | `1D` |
| `ldc.i4.8` | `1E` | InlineNone | `ldc.i4.8` | `1E` |
| `ldc.i4.s` | `1F` | ShortInlineI | `ldc.i4.s 42` | `1F 2A` |
| `ldc.i4` | `20` | InlineI | `ldc.i4 1000` | `20 E8 03 00 00` |
| `ldc.i8` | `21` | InlineI8 | `ldc.i8 1000` | `21 E8 03 00 00 00 00 00 00` |
| `ldc.r4` | `22` | ShortInlineR | `ldc.r4 1.5` | `22 00 00 C0 3F` |
| `ldc.r8` | `23` | InlineR | `ldc.r8 1.5` | `23 00 00 00 00 00 00 F8 3F` |
| `dup` | `25` | InlineNone | `dup` | `25` |
| `pop` | `26` | InlineNone | `pop` | `26` |
| `jmp` | `27` | InlineMethod | `jmp instance void Foo::Bar()` | `27 ?? ?? ?? ??` |
| `call` | `28` | InlineMethod | `call instance void Foo::Bar()` | `28 ?? ?? ?? ??` |
| `calli` | `29` | InlineSig | `calli int32()` | `29 ?? ?? ?? ??` |
| `ret` | `2A` | InlineNone | `ret` | `2A` |
| `br.s` | `2B` | ShortInlineBrTarget | `br.s IL_0000` | `2B FE` |
| `brfalse.s` | `2C` | ShortInlineBrTarget | `brfalse.s IL_0000` | `2C FE` |
| `brtrue.s` | `2D` | ShortInlineBrTarget | `brtrue.s IL_0000` | `2D FE` |
| `beq.s` | `2E` | ShortInlineBrTarget | `beq.s IL_0000` | `2E FE` |
| `bge.s` | `2F` | ShortInlineBrTarget | `bge.s IL_0000` | `2F FE` |
| `bgt.s` | `30` | ShortInlineBrTarget | `bgt.s IL_0000` | `30 FE` |
| `ble.s` | `31` | ShortInlineBrTarget | `ble.s IL_0000` | `31 FE` |
| `blt.s` | `32` | ShortInlineBrTarget | `blt.s IL_0000` | `32 FE` |
| `bne.un.s` | `33` | ShortInlineBrTarget | `bne.un.s IL_0000` | `33 FE` |
| `bge.un.s` | `34` | ShortInlineBrTarget | `bge.un.s IL_0000` | `34 FE` |
| `bgt.un.s` | `35` | ShortInlineBrTarget | `bgt.un.s IL_0000` | `35 FE` |
| `ble.un.s` | `36` | ShortInlineBrTarget | `ble.un.s IL_0000` | `36 FE` |
| `blt.un.s` | `37` | ShortInlineBrTarget | `blt.un.s IL_0000` | `37 FE` |
| `br` | `38` | InlineBrTarget | `br IL_0000` | `38 FB FF FF FF` |
| `brfalse` | `39` | InlineBrTarget | `brfalse IL_0000` | `39 FB FF FF FF` |
| `brtrue` | `3A` | InlineBrTarget | `brtrue IL_0000` | `3A FB FF FF FF` |
| `beq` | `3B` | InlineBrTarget | `beq IL_0000` | `3B FB FF FF FF` |
| `bge` | `3C` | InlineBrTarget | `bge IL_0000` | `3C FB FF FF FF` |
| `bgt` | `3D` | InlineBrTarget | `bgt IL_0000` | `3D FB FF FF FF` |
| `ble` | `3E` | InlineBrTarget | `ble IL_0000` | `3E FB FF FF FF` |
| `blt` | `3F` | InlineBrTarget | `blt IL_0000` | `3F FB FF FF FF` |
| `bne.un` | `40` | InlineBrTarget | `bne.un IL_0000` | `40 FB FF FF FF` |
| `bge.un` | `41` | InlineBrTarget | `bge.un IL_0000` | `41 FB FF FF FF` |
| `bgt.un` | `42` | InlineBrTarget | `bgt.un IL_0000` | `42 FB FF FF FF` |
| `ble.un` | `43` | InlineBrTarget | `ble.un IL_0000` | `43 FB FF FF FF` |
| `blt.un` | `44` | InlineBrTarget | `blt.un IL_0000` | `44 FB FF FF FF` |
| `switch` | `45` | InlineSwitch | `switch (IL_0000, IL_0000)` | `45 02 00 00 00 F3 FF FF FF F3 FF FF FF` |
| `ldind.i1` | `46` | InlineNone | `ldind.i1` | `46` |
| `ldind.u1` | `47` | InlineNone | `ldind.u1` | `47` |
| `ldind.i2` | `48` | InlineNone | `ldind.i2` | `48` |
| `ldind.u2` | `49` | InlineNone | `ldind.u2` | `49` |
| `ldind.i4` | `4A` | InlineNone | `ldind.i4` | `4A` |
| `ldind.u4` | `4B` | InlineNone | `ldind.u4` | `4B` |
| `ldind.i8` | `4C` | InlineNone | `ldind.i8` | `4C` |
| `ldind.i` | `4D` | InlineNone | `ldind.i` | `4D` |
| `ldind.r4` | `4E` | InlineNone | `ldind.r4` | `4E` |
| `ldind.r8` | `4F` | InlineNone | `ldind.r8` | `4F` |
| `ldind.ref` | `50` | InlineNone | `ldind.ref` | `50` |
| `stind.ref` | `51` | InlineNone | `stind.ref` | `51` |
| `stind.i1` | `52` | InlineNone | `stind.i1` | `52` |
| `stind.i2` | `53` | InlineNone | `stind.i2` | `53` |
| `stind.i4` | `54` | InlineNone | `stind.i4` | `54` |
| `stind.i8` | `55` | InlineNone | `stind.i8` | `55` |
| `stind.r4` | `56` | InlineNone | `stind.r4` | `56` |
| `stind.r8` | `57` | InlineNone | `stind.r8` | `57` |
| `add` | `58` | InlineNone | `add` | `58` |
| `sub` | `59` | InlineNone | `sub` | `59` |
| `mul` | `5A` | InlineNone | `mul` | `5A` |
| `div` | `5B` | InlineNone | `div` | `5B` |
| `div.un` | `5C` | InlineNone | `div.un` | `5C` |
| `rem` | `5D` | InlineNone | `rem` | `5D` |
| `rem.un` | `5E` | InlineNone | `rem.un` | `5E` |
| `and` | `5F` | InlineNone | `and` | `5F` |
| `or` | `60` | InlineNone | `or` | `60` |
| `xor` | `61` | InlineNone | `xor` | `61` |
| `shl` | `62` | InlineNone | `shl` | `62` |
| `shr` | `63` | InlineNone | `shr` | `63` |
| `shr.un` | `64` | InlineNone | `shr.un` | `64` |
| `neg` | `65` | InlineNone | `neg` | `65` |
| `not` | `66` | InlineNone | `not` | `66` |
| `conv.i1` | `67` | InlineNone | `conv.i1` | `67` |
| `conv.i2` | `68` | InlineNone | `conv.i2` | `68` |
| `conv.i4` | `69` | InlineNone | `conv.i4` | `69` |
| `conv.i8` | `6A` | InlineNone | `conv.i8` | `6A` |
| `conv.r4` | `6B` | InlineNone | `conv.r4` | `6B` |
| `conv.r8` | `6C` | InlineNone | `conv.r8` | `6C` |
| `conv.u4` | `6D` | InlineNone | `conv.u4` | `6D` |
| `conv.u8` | `6E` | InlineNone | `conv.u8` | `6E` |
| `callvirt` | `6F` | InlineMethod | `callvirt instance void Foo::Bar()` | `6F ?? ?? ?? ??` |
| `cpobj` | `70` | InlineType | `cpobj Foo` | `70 ?? ?? ?? ??` |
| `ldobj` | `71` | InlineType | `ldobj Foo` | `71 ?? ?? ?? ??` |
| `ldstr` | `72` | InlineString | `ldstr "hello"` | `72 ?? ?? ?? ??` |
| `newobj` | `73` | InlineMethod | `newobj instance void Foo::Bar()` | `73 ?? ?? ?? ??` |
| `castclass` | `74` | InlineType | `castclass Foo` | `74 ?? ?? ?? ??` |
| `isinst` | `75` | InlineType | `isinst Foo` | `75 ?? ?? ?? ??` |
| `conv.r.un` | `76` | InlineNone | `conv.r.un` | `76` |
| `unbox` | `79` | InlineType | `unbox Foo` | `79 ?? ?? ?? ??` |
| `throw` | `7A` | InlineNone | `throw` | `7A` |
| `ldfld` | `7B` | InlineField | `ldfld int32 Foo::_value` | `7B ?? ?? ?? ??` |
| `ldflda` | `7C` | InlineField | `ldflda int32 Foo::_value` | `7C ?? ?? ?? ??` |
| `stfld` | `7D` | InlineField | `stfld int32 Foo::_value` | `7D ?? ?? ?? ??` |
| `ldsfld` | `7E` | InlineField | `ldsfld int32 Foo::_value` | `7E ?? ?? ?? ??` |
| `ldsflda` | `7F` | InlineField | `ldsflda int32 Foo::_value` | `7F ?? ?? ?? ??` |
| `stsfld` | `80` | InlineField | `stsfld int32 Foo::_value` | `80 ?? ?? ?? ??` |
| `stobj` | `81` | InlineType | `stobj Foo` | `81 ?? ?? ?? ??` |
| `conv.ovf.i1.un` | `82` | InlineNone | `conv.ovf.i1.un` | `82` |
| `conv.ovf.i2.un` | `83` | InlineNone | `conv.ovf.i2.un` | `83` |
| `conv.ovf.i4.un` | `84` | InlineNone | `conv.ovf.i4.un` | `84` |
| `conv.ovf.i8.un` | `85` | InlineNone | `conv.ovf.i8.un` | `85` |
| `conv.ovf.u1.un` | `86` | InlineNone | `conv.ovf.u1.un` | `86` |
| `conv.ovf.u2.un` | `87` | InlineNone | `conv.ovf.u2.un` | `87` |
| `conv.ovf.u4.un` | `88` | InlineNone | `conv.ovf.u4.un` | `88` |
| `conv.ovf.u8.un` | `89` | InlineNone | `conv.ovf.u8.un` | `89` |
| `conv.ovf.i.un` | `8A` | InlineNone | `conv.ovf.i.un` | `8A` |
| `conv.ovf.u.un` | `8B` | InlineNone | `conv.ovf.u.un` | `8B` |
| `box` | `8C` | InlineType | `box Foo` | `8C ?? ?? ?? ??` |
| `newarr` | `8D` | InlineType | `newarr Foo` | `8D ?? ?? ?? ??` |
| `ldlen` | `8E` | InlineNone | `ldlen` | `8E` |
| `ldelema` | `8F` | InlineType | `ldelema Foo` | `8F ?? ?? ?? ??` |
| `ldelem.i1` | `90` | InlineNone | `ldelem.i1` | `90` |
| `ldelem.u1` | `91` | InlineNone | `ldelem.u1` | `91` |
| `ldelem.i2` | `92` | InlineNone | `ldelem.i2` | `92` |
| `ldelem.u2` | `93` | InlineNone | `ldelem.u2` | `93` |
| `ldelem.i4` | `94` | InlineNone | `ldelem.i4` | `94` |
| `ldelem.u4` | `95` | InlineNone | `ldelem.u4` | `95` |
| `ldelem.i8` | `96` | InlineNone | `ldelem.i8` | `96` |
| `ldelem.i` | `97` | InlineNone | `ldelem.i` | `97` |
| `ldelem.r4` | `98` | InlineNone | `ldelem.r4` | `98` |
| `ldelem.r8` | `99` | InlineNone | `ldelem.r8` | `99` |
| `ldelem.ref` | `9A` | InlineNone | `ldelem.ref` | `9A` |
| `stelem.i` | `9B` | InlineNone | `stelem.i` | `9B` |
| `stelem.i1` | `9C` | InlineNone | `stelem.i1` | `9C` |
| `stelem.i2` | `9D` | InlineNone | `stelem.i2` | `9D` |
| `stelem.i4` | `9E` | InlineNone | `stelem.i4` | `9E` |
| `stelem.i8` | `9F` | InlineNone | `stelem.i8` | `9F` |
| `stelem.r4` | `A0` | InlineNone | `stelem.r4` | `A0` |
| `stelem.r8` | `A1` | InlineNone | `stelem.r8` | `A1` |
| `stelem.ref` | `A2` | InlineNone | `stelem.ref` | `A2` |
| `ldelem` | `A3` | InlineType | `ldelem Foo` | `A3 ?? ?? ?? ??` |
| `stelem` | `A4` | InlineType | `stelem Foo` | `A4 ?? ?? ?? ??` |
| `unbox.any` | `A5` | InlineType | `unbox.any Foo` | `A5 ?? ?? ?? ??` |
| `conv.ovf.i1` | `B3` | InlineNone | `conv.ovf.i1` | `B3` |
| `conv.ovf.u1` | `B4` | InlineNone | `conv.ovf.u1` | `B4` |
| `conv.ovf.i2` | `B5` | InlineNone | `conv.ovf.i2` | `B5` |
| `conv.ovf.u2` | `B6` | InlineNone | `conv.ovf.u2` | `B6` |
| `conv.ovf.i4` | `B7` | InlineNone | `conv.ovf.i4` | `B7` |
| `conv.ovf.u4` | `B8` | InlineNone | `conv.ovf.u4` | `B8` |
| `conv.ovf.i8` | `B9` | InlineNone | `conv.ovf.i8` | `B9` |
| `conv.ovf.u8` | `BA` | InlineNone | `conv.ovf.u8` | `BA` |
| `refanyval` | `C2` | InlineType | `refanyval Foo` | `C2 ?? ?? ?? ??` |
| `ckfinite` | `C3` | InlineNone | `ckfinite` | `C3` |
| `mkrefany` | `C6` | InlineType | `mkrefany Foo` | `C6 ?? ?? ?? ??` |
| `ldtoken` | `D0` | InlineTok | `ldtoken Foo` | `D0 ?? ?? ?? ??` |
| `conv.u2` | `D1` | InlineNone | `conv.u2` | `D1` |
| `conv.u1` | `D2` | InlineNone | `conv.u1` | `D2` |
| `conv.i` | `D3` | InlineNone | `conv.i` | `D3` |
| `conv.ovf.i` | `D4` | InlineNone | `conv.ovf.i` | `D4` |
| `conv.ovf.u` | `D5` | InlineNone | `conv.ovf.u` | `D5` |
| `add.ovf` | `D6` | InlineNone | `add.ovf` | `D6` |
| `add.ovf.un` | `D7` | InlineNone | `add.ovf.un` | `D7` |
| `mul.ovf` | `D8` | InlineNone | `mul.ovf` | `D8` |
| `mul.ovf.un` | `D9` | InlineNone | `mul.ovf.un` | `D9` |
| `sub.ovf` | `DA` | InlineNone | `sub.ovf` | `DA` |
| `sub.ovf.un` | `DB` | InlineNone | `sub.ovf.un` | `DB` |
| `endfinally` | `DC` | InlineNone | `endfinally` | `DC` |
| `leave` | `DD` | InlineBrTarget | `leave IL_0000` | `DD FB FF FF FF` |
| `leave.s` | `DE` | ShortInlineBrTarget | `leave.s IL_0000` | `DE FE` |
| `stind.i` | `DF` | InlineNone | `stind.i` | `DF` |
| `conv.u` | `E0` | InlineNone | `conv.u` | `E0` |
| `arglist` | `FE 00` | InlineNone | `arglist` | `FE 00` |
| `ceq` | `FE 01` | InlineNone | `ceq` | `FE 01` |
| `cgt` | `FE 02` | InlineNone | `cgt` | `FE 02` |
| `cgt.un` | `FE 03` | InlineNone | `cgt.un` | `FE 03` |
| `clt` | `FE 04` | InlineNone | `clt` | `FE 04` |
| `clt.un` | `FE 05` | InlineNone | `clt.un` | `FE 05` |
| `ldftn` | `FE 06` | InlineMethod | `ldftn instance void Foo::Bar()` | `FE 06 ?? ?? ?? ??` |
| `ldvirtftn` | `FE 07` | InlineMethod | `ldvirtftn instance void Foo::Bar()` | `FE 07 ?? ?? ?? ??` |
| `ldarg` | `FE 09` | InlineVar | `ldarg 4` | `FE 09 04 00` |
| `ldarga` | `FE 0A` | InlineVar | `ldarga 4` | `FE 0A 04 00` |
| `starg` | `FE 0B` | InlineVar | `starg 4` | `FE 0B 04 00` |
| `ldloc` | `FE 0C` | InlineVar | `ldloc 4` | `FE 0C 04 00` |
| `ldloca` | `FE 0D` | InlineVar | `ldloca 4` | `FE 0D 04 00` |
| `stloc` | `FE 0E` | InlineVar | `stloc 4` | `FE 0E 04 00` |
| `localloc` | `FE 0F` | InlineNone | `localloc` | `FE 0F` |
| `endfilter` | `FE 11` | InlineNone | `endfilter` | `FE 11` |
| `unaligned.` | `FE 12` | ShortInlineI | `unaligned. 42` | `FE 12 2A` |
| `volatile.` | `FE 13` | InlineNone | `volatile.` | `FE 13` |
| `tail.` | `FE 14` | InlineNone | `tail.` | `FE 14` |
| `initobj` | `FE 15` | InlineType | `initobj Foo` | `FE 15 ?? ?? ?? ??` |
| `constrained.` | `FE 16` | InlineType | `constrained. Foo` | `FE 16 ?? ?? ?? ??` |
| `cpblk` | `FE 17` | InlineNone | `cpblk` | `FE 17` |
| `initblk` | `FE 18` | InlineNone | `initblk` | `FE 18` |
| `rethrow` | `FE 1A` | InlineNone | `rethrow` | `FE 1A` |
| `sizeof` | `FE 1C` | InlineType | `sizeof Foo` | `FE 1C ?? ?? ?? ??` |
| `refanytype` | `FE 1D` | InlineNone | `refanytype` | `FE 1D` |
| `readonly.` | `FE 1E` | InlineNone | `readonly.` | `FE 1E` |
