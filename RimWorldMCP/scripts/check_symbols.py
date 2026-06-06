"""
check_symbols.py - Validate Symbols.json

Checks:
  1. symbols: 每条记录有 char（单字符）和 group 字段
  2. symbols: 所有字符唯一（一对一映射）
  3. symbols: 无字符与固定网格冲突（fertility/temperature/pollution）
  4. symbols: 无控制字符
  5. fallback_pool: 字段存在且为数组
  6. fallback_pool: 字符不在 symbols 中（无重复分配）
  7. fallback_pool: 内部无重复
  8. fallback_pool: 字符不在固定网格中

Usage:
  python3 check_symbols.py RimWorldMCP/resource/Symbols.json

Exit: 0=pass, 1=fail
"""

import json
import sys
import unicodedata
from collections import Counter

RESERVED = set("▓▒░·○◎●█P.?")


def main():
    path = sys.argv[1]

    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    errors = []

    # ---- symbols ----
    symbols = data.get("symbols")
    if not isinstance(symbols, dict):
        errors.append("missing or invalid 'symbols' field")
    else:
        char_to_def = {}
        chars = []

        for def_name, entry in symbols.items():
            if not isinstance(entry, dict):
                errors.append(f"{def_name}: not an object")
                continue

            ch = entry.get("char")
            grp = entry.get("group")

            if not ch or not isinstance(ch, str) or len(ch) != 1:
                errors.append(f"{def_name}: invalid char ({ch!r})")
                continue

            chars.append(ch)

            if ch in char_to_def:
                errors.append(f"dup: '{ch}' used by {char_to_def[ch]} and {def_name}")
            else:
                char_to_def[ch] = def_name

            if ch in RESERVED:
                errors.append(f"reserved: {def_name} char '{ch}' used by fixed grid")

            cat = unicodedata.category(ch)
            if cat.startswith("C") and cat != "Co":
                errors.append(f"control: {def_name} '{ch}' (U+{ord(ch):04X})")

            if not grp:
                errors.append(f"no group: {def_name}")

        dupes = {c: n for c, n in Counter(chars).items() if n > 1}
        if dupes:
            for c, n in dupes.items():
                names = [k for k, v in symbols.items() if v.get("char") == c]
                errors.append(f"dup char '{c}' x{n}: {names}")

    # ---- fallback_pool ----
    pool = data.get("fallback_pool")
    symbol_chars = set(v["char"] for v in symbols.values()) if isinstance(symbols, dict) else set()

    if not isinstance(pool, list):
        errors.append("missing or invalid 'fallback_pool' field")
    else:
        pool_dupes = {c: n for c, n in Counter(pool).items() if n > 1}
        if pool_dupes:
            for c, n in pool_dupes.items():
                errors.append(f"fallback_pool dup: '{c}' x{n}")

        for ch in pool:
            if not isinstance(ch, str) or len(ch) != 1:
                errors.append(f"fallback_pool: invalid char ({ch!r})")
            elif ch in symbol_chars:
                errors.append(f"fallback_pool: '{ch}' already used in symbols")
            elif ch in RESERVED:
                errors.append(f"fallback_pool: '{ch}' is reserved by fixed grid")
            elif unicodedata.category(ch).startswith("C") and unicodedata.category(ch) != "Co":
                errors.append(f"fallback_pool: '{ch}' (U+{ord(ch):04X}) is control char")

    if errors:
        print(f"FAIL - {len(errors)} errors:")
        for e in errors:
            print(f"  {e}")
        sys.exit(1)
    else:
        print(f"OK - symbols: {len(symbols)}, fallback_pool: {len(pool)}, all chars unique")


if __name__ == "__main__":
    main()
