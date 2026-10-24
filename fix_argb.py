# -*- coding: utf-8 -*-
"""把 WPF 的 Color.FromArgb(r,g,b) 三参写法补成四参（补 alpha=255）。
WPF 的 Color.FromArgb 只有 (a,r,g,b) 重载，三参会编译失败。"""
import io, re, os, sys

base = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src')
changed = []
for fn in os.listdir(base):
    if not fn.endswith('.cs'):
        continue
    p = os.path.join(base, fn)
    s = io.open(p, encoding='utf-8').read()
    def fix(m):
        parts = [x.strip() for x in m.group(1).split(',')]
        if len(parts) == 3:
            return 'Color.FromArgb(255, ' + ', '.join(parts) + ')'
        return m.group(0)
    s2 = re.sub(r'Color\.FromArgb\(([^()]*)\)', fix, s)
    if s2 != s:
        io.open(p, 'w', encoding='utf-8').write(s2)
        changed.append(fn)
print('fixed:', ', '.join(changed) if changed else 'none')
