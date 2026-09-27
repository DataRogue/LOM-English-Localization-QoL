"""The rows of a StringTable.csv with the physical lines each one spans, parsed exactly like migrate_standalone.idea_parse
(Ideafixxxer.CsvParser: physical lines split at LF or CRLF, empty lines skipped, a line break inside quotes kept as CRLF).
rewrite(raw, edits) replaces whole rows by key, so values that span several physical lines are handled; everything else in the
file stays byte for byte."""
import re

SPLIT = re.compile('\r\n|\n')


def spans(raw):
    """Yield (first_line_index, last_line_index, fields) per logical row; indexes into physical_lines(raw)."""
    lines = physical_lines(raw)
    cur, val, st = [], [], 'LS'
    start = None
    for idx, (text, _) in enumerate(lines):
        if len(text) == 0:
            continue
        if start is None:
            start = idx
        for c in text:
            if c == ',':
                if st in ('LS', 'VS', 'V'):
                    cur.append(''.join(val)); val = []; st = 'VS'
                elif st == 'Q':
                    val.append(',')
                elif st == 'QQ':
                    cur.append(''.join(val)); val = []; st = 'VS'
            elif c == '"':
                if st in ('LS', 'VS'):
                    st = 'Q'
                elif st == 'V':
                    val.append('"')
                elif st == 'Q':
                    st = 'QQ'
                elif st == 'QQ':
                    val.append('"'); st = 'Q'
            else:
                if st in ('LS', 'VS', 'V'):
                    val.append(c); st = 'V'
                elif st == 'Q':
                    val.append(c)
                elif st == 'QQ':
                    val.append(c); st = 'Q'
        if st == 'LS':
            yield start, idx, cur
            cur = []; start = None
        elif st in ('VS', 'V', 'QQ'):
            cur.append(''.join(val)); val = []
            yield start, idx, cur
            cur = []; start = None; st = 'LS'
        elif st == 'Q':
            val.append('\r'); val.append('\n')
    if val:
        cur.append(''.join(val))
    if cur:
        yield start, len(lines) - 1, cur


def physical_lines(raw):
    """[(text, line_end)] keeping each line's own ending ('\\r\\n', '\\n' or '' for the last)."""
    out = []
    pos = 0
    for m in SPLIT.finditer(raw):
        out.append((raw[pos:m.start()], m.group(0)))
        pos = m.end()
    out.append((raw[pos:], ''))
    return out


def value_of(fields):
    return ','.join(fields[1:]) if len(fields) > 2 else (fields[1] if len(fields) > 1 else '')


def serialize(key, value, nl):
    """key,"value" with quotes doubled; line breaks inside the value written as the file's own line ending."""
    v = value.replace('\r\n', '\n').replace('\n', nl)
    return key + ',"' + v.replace('"', '""') + '"'


def rewrite(raw, edits, nl):
    """edits: key -> (old_value, new_value). Replaces the first row of each key whose parsed value equals old_value.
    Returns (new_raw, applied_keys, mismatched_keys)."""
    lines = physical_lines(raw)
    applied = set()
    mismatched = set()
    replace = {}
    seen = set()
    for s, e, fields in spans(raw):
        if not fields or fields[0] in seen:
            continue
        seen.add(fields[0])
        k = fields[0]
        if k in edits:
            old, new = edits[k]
            if value_of(fields) == old:
                replace[s] = (e, serialize(k, new, nl))
                applied.add(k)
            else:
                mismatched.add(k)
    out = []
    i = 0
    while i < len(lines):
        if i in replace:
            e, text = replace[i]
            out.append(text + lines[e][1])
            i = e + 1
            continue
        out.append(lines[i][0] + lines[i][1])
        i += 1
    return ''.join(out), applied, mismatched
