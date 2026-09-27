# Conventions for the Legend of Mortal (活俠傳) English glossary

## Owner's standing instruction
Use the established Western wuxia nomenclature: the terms that readers of translated wuxia novels (Jin Yong / Gu Long
official and fan translations, Wuxiaworld-style glossaries) and players of localized wuxia games already know.
Owner's own example: 心法 = "Internal Method" (allowed alt "Inner Method"), NOT "Heart Method" (which the base
patch glossary still uses). Prefer the recognized term over a literal calque; prefer English over pinyin except for
terms that are conventionally left in pinyin (Jianghu, Wulin, Qinggong, Qi, names of people, sects, places).

## Binding rulings already made in the owner's override layer (BepInEx/plugins/LOM_UI_EN/strings/*.csv)
- 葉雲舟 = Ye Yunzhou (舟 is zhōu). "Ye Yunzhao" is the corpus-wide WRONG form (276 uses in the base patch).
- 葉雲裳 = Ye Yunshang (裳 read shang, not chang). "Yun Chang" (254 uses) is wrong.
- 唐默鈴 = Tang Moling (默 mò); "Tang Meling" is wrong.
- Reduplicated given names join: 唐嬌嬌 = Tang Jiaojiao.
- 點蒼 = Diancang, never split; 點蒼劍法 = Diancang Sword Technique (never pinyin "Jianfa").
- 無相祖師 = Patriarch Wuxiang (title first). 金烏上人 = Venerable Golden Crow (上人 = Venerable).
- 大俠 = Great Hero; 少俠 = Young Hero; 女俠 = Heroine. Keep the register distinction; never drop Great/Young.
- 山賊 = Mountain Bandits (team) / Mountain Bandit (one enemy).
- 抗毒 = Poison Resistance; 抗麻 = Paralysis Resistance (noun phrases).
- 修練 = Training; 修養 = Cultivation (the stat). Never call 修練 "Cultivation".
- 輕功 = Qinggong, never "light steps" / "lightness skill".
- 樁 = stance (standing stance), never "stake".
- 不壞 = indestructible; 無敵 = invincible (distinct terms).
- 羅漢 stays pinyin "Luohan"; 拳 = Fist (Luohan Fist Manual). Do not mix "Quan" and "Fist".
- 練功場 = Training Ground (Title Case place). 講經堂/講經樓 = Scripture Hall. 藥鋪 (Li the doctor's shop) = Apothecary, not Pharmacy.
- 兩 = tael(s) (money), never "jin".
- 絕招 in combat UI = "Ultimate" (owner kept it for the action card); 極招 = "Extreme Move" (First/Second/Third Extreme Move).
- 捅人 (combat command) = Stab; 防禦/挨打 command = Brace; 嘴力 attack = Verbal Attack.
The full override table is ctx_lom_overrides.tsv (key, text, note). Grep it for any term before proposing a change:
if the owner already ruled, keep the ruling and cite the key.

## Established base-patch terms (keep unless demonstrably wrong; changing any of these needs_user_decision=true)
Tang Clan (唐門), Wulin Alliance (武林盟), Alliance Leader (盟主), Jianghu, Wulin, Central Plains (中原),
Imperial Court (朝廷), Flying Stone Gang (飛石幫), Beggar Sect (丐幫), Jinxiang Palace (錦香宮),
Thousand Lantern Tower (千燈樓), Kongtong Sect (崆峒), Qingcheng (青城), Songshan (嵩山), Emei (峨嵋),
Quanzhen (全真), Diancang (點蒼), Snow Mountain Sect (雪山派), Nangong Family (南宮世家), Shangguan Family (上官世家),
Zhao Huo (趙活, the protagonist), Sect Leader (掌門), Gang Leader (幫主), Senior/Junior Brother/Sister with ordinals.

## Name rules
- Pinyin, surname first, given-name syllables joined with no hyphen or space: Zhao Huo, Ye Yunzhou, Yu Xiaomei,
  Xiahou Lan, Shangguan Ying, Nangong Shen. Two-character surnames stay joined (Shangguan, Nangong, Xiahou, Shentu, Huyan).
- Verify the reading of every character; note tone-ambiguous or polyphonic characters (舟 zhōu, 裳 shang, 默 mò, 湘 xiāng, 啾 jiū, 淺 qiǎn, 隼 sǔn).
- Nicknames and epithets are translated (Thousand-Faced Demon, Old Soul Reaper), hyphenate compound modifiers.
- 阿X familiar prefix: the base patch uses A'Huo / A'Qian; keep that form. 兒 diminutive suffix: Shen'er, Qian'er, Ying'er.
- Historical persons use their standard Western spellings (Genghis Khan, Qiu Chuji, Emperor Ningzong).

## Organization suffixes (default; flag any conflict)
派 Sect | 幫 Gang | 教 Cult (泥教, 極樂教, 魔教) unless the base has an entrenched "Sect" | 門 Clan only for the
family-run Tang Clan, otherwise Sect (flag) | 宮 Palace | 樓 Tower/House | 山莊 Manor | 鏢局 Escort Agency |
世家 Family | 堂 Hall | 閣 Pavilion | 寨 Fort/Stronghold | 莊 Manor/Village (by context) | 派系 faction.

## Titles and honorifics
大俠 Great Hero; 少俠 Young Hero; 女俠 Heroine; 上人 Venerable; 祖師 Patriarch; 真人 (Daoist) — propose and flag;
道長 Daoist (as title: "Daoist Shentu"); 大師 Master (monk); 禪師 Chan Master; 前輩 Senior; 晚輩 junior; 施主 Benefactor;
公子 Young Lord (base) — propose Young Master only with rationale; 郡主 Lady (Lady Ningyang); 員外 Squire/Master;
掌門 Sect Leader; 幫主 Gang Leader; 盟主 Alliance Leader; 堂主 Hall Master; 長老 Elder; 護法 Protector/Guardian (flag);
師父 Master; 師兄 Senior Brother; 大師兄 Eldest Senior Brother (check owner's table); 小師妹 Little Junior Sister.

## Mechanics (game systems; use the code enums for the exact set)
Stats (GameStatType): 體力 Stamina, 內力 Internal Force (flag vs "Inner Power"), 輕功 Qinggong, 銀兩 Silver Taels,
魅力 Charm, 學問 Learning/Scholarship, 心理衛生 Mental Health, 命運 Fate, 性情 Temperament, 處世 Worldliness,
修養 Cultivation, 道德 Morality, 嘴力 Verbal Power (flag), 門派規模 Sect Size, 門派名聲 Sect Reputation,
向心力 Cohesion, 鍛造 Forging, 毒藥 Poison, 廚藝 Cooking, 儒學/道學/佛學 Confucian/Daoist/Buddhist Studies.
Martial types (MartialType): 刀劍 Blades & Swords, 拳掌 Fists & Palms, 腿法 Kicks/Leg Techniques, 暗器 Hidden Weapons,
奇門 Unorthodox Arms (flag), 軟兵器 Flexible Weapons, 槍棍 Spears & Staves, 內功 Internal Arts/Internal Skills (flag), 特殊 Special.
Core wuxia vocabulary to settle as a FAMILY (must be mutually consistent; flag the whole family for the owner):
心法 Internal Method | 內功 Internal Skill(s)/Internal Arts | 內力 Internal Force | 真氣 True Qi | 氣 Qi | 氣血 Vitality/Lifeforce |
境界 Realm | 招式 Move | 絕招 Ultimate (UI) / signature move (prose) | 武功 Martial Arts | 武學 Martial Learning |
秘笈 Secret Manual | 心訣 Mental Formula/Oral Formula | 穴道 Acupoint | 點穴 Acupoint Strike | 經脈 Meridians |
走火入魔 Qi Deviation | 江湖 Jianghu | 武林 Wulin | 門派 Sect | 俠 hero/chivalrous | 高手 Expert/Master.

## Register and format
Title Case for the names of things (skills, techniques, manuals, items, places, titles used as names);
sentence case for descriptions. Keep English terse enough for UI boxes (the owner's override notes record widths).

## Owner rulings of 2026-09-12 (answers to the 23 glossary questions; binding)
1. 娘子 as an address = Lady X (Lady Xin, Lady Shangguan); bare spousal sense stays "wife"; 小娘子 = Young Lady X.
2. 釋明 = Shi Ming (two words; 釋 is the Buddhist surname), 慧明 = Huiming; compounds follow (Master Shi Ming, Martial Uncle Shi Ming). Not "Shiming".
3. 武林大會 = Wulin Assembly; 西武林大會 = Western Wulin Assembly.
4. 內力 = Internal Force everywhere (stat, items, skill text). Never Inner Strength / Inner Power. 內功 = Internal Skill, 心法 = Internal Method, 真氣 = True Qi.
5. 老爺 = Master (X老爺 = Master X); 少爺 = Young Master (X少爺 = Young Master X); 老太爺 = Old Patriarch. 公子 stays Young Lord; 相公 stays Young Master.
6. 真人 = Immortal X (Immortal Qiu, Immortal Zou, Immortal Changchun, Fire Dragon Immortal). Never Reverend / Perfected / Daoist Master / Zhenren.
7. X大人 = the office title when the script names the office (Magistrate Song ...); Lord X only when no office is named. Bare 大人 vocative = my lord.
8. 大哥 between non-sect friends = Brother X. Senior Brother is exclusive to 師兄.
9. Surname 解 = Xie: Xie Wuchen, Great Hero Xie, Young Lord Xie, Wind God Xie Wuchen. Never Jie.
10. 趙郎 (and any spousal X郎 address) = an English endearment per line (my dear, dearest, Husband). Never Zhao Lang / Brother Zhao.
11. 護法 = Guardian for the whole family (Left/Right Guardian, Earth/Water/Fire/Wind Guardian, Four Guardians of Kongtong, Flying Heaven Guardian, Right Guardian of the Supreme Bliss Cult). Never Protector.
12. 師叔 = Martial Uncle (X師叔 = Martial Uncle X); 師伯 = Senior Martial Uncle; 師姑 = Martial Aunt; 師叔祖 = Grand Martial Uncle. Never plain Uncle / Master Uncle.
13. 極招 = Ultimate (same word as 絕招): First/Second/Third/Fourth Ultimate, Sword Saint's First Ultimate, Four Ultimates. Never Extreme Move.
14. 異常狀態 = Status Effect (matches StateEffectType). Not Status Ailment / Abnormal Status.
15. 奪魄 = Soul-Stealing in every compound (Orchid, Gate, Forest, Peak, Phantom Claw, Disciple). Never Soul-Stealer.
16. 奪魄幽蘭 = Soul-Stealing Orchid; 幽蘭師父 = Master Orchid; full title "Heroine Xiahou, the Soul-Stealing Orchid". Never Secluded Orchid / Youlan.
17. Surname 樂 (the courtesan) = Yue: Yue Ping, Lady Yue, Young Lady Yue, Lady Yue Ping. Never Le Ping.
18. 舵主 = Branch Leader (Branch Leader Yang, Dayi Branch Leader). Never Helmsman / Branch Chief.
19. 定遠郎 = Commandant Dingyuan (Commandant Dingyuan Wang Shi); 忠國公 = Duke of Loyalty.
20. Named leg techniques ending in 腳 = Kick (Thunder God Kick, Flying Heaven Kick, Heart Sword · Thunder God Kick). Never Foot.
21. 風流扇 = Dashing Fan (Dashing Fan Skill / Technique, Nangong Dashing Fan). Never Flowery.
22. 折花手 = Flower Plucking Hand; 捻花指 = Flower-Twirling Finger; 拈花手 = Flower-Twirling Hand.
23. One-off calls: accept the arbiter recommendations (上官隼 = Shangguan Sun, 阿隼 = A'Sun; 萬靈油 = Panacea Oil; 苦惱拳 = Suffering Fist; 奇門 = Unorthodox Arms; 師弟 = Junior Brother; 唐老施主 = Benefactor Tang).
