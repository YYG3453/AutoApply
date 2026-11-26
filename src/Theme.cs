using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace AutoApply
{
    /// <summary>
    /// 视觉语言：夜（墨底 + 玉色 + 香槟金细线）/ 日（宣纸底 + 同色系加深）。
    /// 所有绘制都走这里，主题切换只改这一处调色板，保证风格统一。
    /// </summary>
    internal static class T
    {
        /// <summary>true = 夜晚模式（默认），false = 日间模式。</summary>
        public static bool Night = true;

        // ---------- 底色 ----------
        public static Color Bg;
        public static Color BgTop;
        public static Color Bg2;
        public static Color Surface;
        public static Color SurfaceHi;
        public static Color Line;
        public static Color LineSoft;

        // ---------- 面层（按深浅排开，供自绘控件取用）----------
        public static Color RailTop;
        public static Color RailSel;
        public static Color Panel2;
        public static Color Panel3;
        public static Color Panel4;
        public static Color Field;
        public static Color Console;
        public static Color HoverSoft;
        public static Color HoverSoftHi;
        public static Color HoverSoft2;
        public static Color BtnHover;
        public static Color BtnHover2;
        public static Color Track;
        public static Color LineHi;
        public static Color Line2;
        public static Color Line3;

        // ---------- 文字 ----------
        public static Color Text;
        public static Color Text2;
        public static Color Text3;
        public static Color TextSoft;

        // ---------- 强调 ----------
        public static Color Jade;
        public static Color JadeDim;
        public static Color JadeHi;
        public static Color JadeMid;
        public static Color JadeLight;
        public static Color Jade2;
        public static Color JadeDeep;
        public static Color JadeInk;
        public static Color JadeInk2;
        public static Color Gold;
        public static Color Warn;
        public static Color Danger;
        public static Color Blue;

        static T() { SetNight(true); }

        /// <summary>切换主题调色板。切完要调用方重绘。</summary>
        public static void SetNight(bool night)
        {
            Night = night;
            if (night)
            {
                Bg = Color.FromArgb(0x0B, 0x0D, 0x11);
                BgTop = Color.FromArgb(0x0D, 0x10, 0x15);
                Bg2 = Color.FromArgb(0x11, 0x14, 0x1A);
                Surface = Color.FromArgb(0x15, 0x18, 0x1F);
                SurfaceHi = Color.FromArgb(0x1B, 0x20, 0x28);
                Line = Color.FromArgb(0x27, 0x2C, 0x36);
                LineSoft = Color.FromArgb(0x1E, 0x22, 0x2A);

                RailTop = Color.FromArgb(0x14, 0x18, 0x1F);
                RailSel = Color.FromArgb(0x1A, 0x21, 0x28);
                Panel2 = Color.FromArgb(0x12, 0x16, 0x1C);
                Panel3 = Color.FromArgb(0x18, 0x1D, 0x24);
                Panel4 = Color.FromArgb(0x15, 0x19, 0x20);
                Field = Color.FromArgb(0x18, 0x1C, 0x23);
                Console = Color.FromArgb(0x0E, 0x11, 0x16);
                HoverSoft = Color.FromArgb(0x18, 0x1D, 0x25);
                HoverSoftHi = Color.FromArgb(0x24, 0x2B, 0x35);
                HoverSoft2 = Color.FromArgb(0x24, 0x29, 0x31);
                BtnHover = Color.FromArgb(0x22, 0x28, 0x31);
                BtnHover2 = Color.FromArgb(0x22, 0x27, 0x30);
                Track = Color.FromArgb(0x26, 0x2D, 0x37);
                LineHi = Color.FromArgb(0x33, 0x3A, 0x46);
                Line2 = Color.FromArgb(0x2A, 0x30, 0x39);
                Line3 = Color.FromArgb(0x2A, 0x30, 0x3A);

                Text = Color.FromArgb(0xEC, 0xEE, 0xF3);
                Text2 = Color.FromArgb(0x9A, 0xA3, 0xB2);
                Text3 = Color.FromArgb(0x63, 0x6B, 0x79);
                TextSoft = Color.FromArgb(0xC9, 0xCF, 0xDA);

                Jade = Color.FromArgb(0x6F, 0xE3, 0xBE);
                JadeDim = Color.FromArgb(0x37, 0x8B, 0x74);
                JadeHi = Color.FromArgb(0x8A, 0xEC, 0xC7);
                JadeMid = Color.FromArgb(0x50, 0xC6, 0xA4);
                JadeLight = Color.FromArgb(0xA6, 0xF5, 0xD8);
                Jade2 = Color.FromArgb(0x5F, 0xD8, 0xB4);
                JadeDeep = Color.FromArgb(0x2F, 0x8E, 0x76);
                JadeInk = Color.FromArgb(0x07, 0x24, 0x1C);
                JadeInk2 = Color.FromArgb(0x08, 0x2A, 0x22);
                Gold = Color.FromArgb(0xD9, 0xB2, 0x6A);
                Warn = Color.FromArgb(0xE8, 0x9B, 0x5C);
                Danger = Color.FromArgb(0xE0, 0x6C, 0x6C);
                Blue = Color.FromArgb(0x6C, 0xA8, 0xE0);
            }
            else
            {
                // 宣纸：暖白底 + 墨字 + 同色系加深的玉色，白天也保持"安静"的对比度
                Bg = Color.FromArgb(0xF4, 0xF1, 0xEA);
                BgTop = Color.FromArgb(0xFD, 0xFB, 0xF7);
                Bg2 = Color.FromArgb(0xEF, 0xEB, 0xE2);
                Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
                SurfaceHi = Color.FromArgb(0xFA, 0xF7, 0xF1);
                Line = Color.FromArgb(0xE3, 0xDC, 0xCD);
                LineSoft = Color.FromArgb(0xEC, 0xE7, 0xDB);

                RailTop = Color.FromArgb(0xFF, 0xFF, 0xFF);
                RailSel = Color.FromArgb(0xF1, 0xEC, 0xE1);
                Panel2 = Color.FromArgb(0xFC, 0xFA, 0xF5);
                Panel3 = Color.FromArgb(0xF8, 0xF5, 0xEE);
                Panel4 = Color.FromArgb(0xF7, 0xF4, 0xEC);
                Field = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Console = Color.FromArgb(0xFB, 0xF9, 0xF3);
                HoverSoft = Color.FromArgb(0xF2, 0xEE, 0xE4);
                HoverSoftHi = Color.FromArgb(0xE9, 0xE4, 0xD6);
                HoverSoft2 = Color.FromArgb(0xEF, 0xEB, 0xE0);
                BtnHover = Color.FromArgb(0xED, 0xE8, 0xDB);
                BtnHover2 = Color.FromArgb(0xE7, 0xE1, 0xD2);
                Track = Color.FromArgb(0xE2, 0xDC, 0xCC);
                LineHi = Color.FromArgb(0xD6, 0xCF, 0xBE);
                Line2 = Color.FromArgb(0xDC, 0xD5, 0xC5);
                Line3 = Color.FromArgb(0xD3, 0xCC, 0xBB);

                Text = Color.FromArgb(0x1A, 0x1D, 0x23);
                Text2 = Color.FromArgb(0x5B, 0x62, 0x70);
                Text3 = Color.FromArgb(0x8E, 0x96, 0xA2);
                TextSoft = Color.FromArgb(0x33, 0x38, 0x3F);

                Jade = Color.FromArgb(0x14, 0x8A, 0x69);
                JadeDim = Color.FromArgb(0x6E, 0xBE, 0xA6);
                JadeHi = Color.FromArgb(0x36, 0xB4, 0x8D);
                JadeMid = Color.FromArgb(0x1E, 0x9E, 0x7A);
                JadeLight = Color.FromArgb(0x0F, 0x6B, 0x50);
                Jade2 = Color.FromArgb(0x27, 0xA8, 0x82);
                JadeDeep = Color.FromArgb(0x11, 0x6E, 0x55);
                JadeInk = Color.FromArgb(0xFF, 0xFF, 0xFF);
                JadeInk2 = Color.FromArgb(0xEC, 0xFF, 0xF8);
                Gold = Color.FromArgb(0xA8, 0x7B, 0x2E);
                Warn = Color.FromArgb(0xB8, 0x6C, 0x24);
                Danger = Color.FromArgb(0xC0, 0x41, 0x3C);
                Blue = Color.FromArgb(0x2C, 0x6B, 0xB0);
            }
            if (night) ApplyNightStyle(NightStyle);
            // GitHub 是唯一一套"日夜都成立"的皮肤：白天走 Primer Light，夜里走 Primer Dark。
            else if (NightStyle == 5) ApplyGithub(false);
        }

        /// <summary>夜晚模式的备选配色（0 石墨灰 / 1 墨蓝夜 / 2 暖夜 / 3 深海蓝 / 4 深蓝 / 5 GitHub）。</summary>
        public static int NightStyle = 4;

        /// <summary>皮肤名字：右键菜单、Toast 文案共用这一张表，顺序就是编号。</summary>
        public static readonly string[] StyleNames = { "石墨灰", "墨蓝夜", "暖夜", "深海蓝", "深蓝", "GitHub" };

        public static string StyleName(int style)
        {
            if (style < 0) style = 0;
            if (style >= StyleNames.Length) style = StyleNames.Length - 1;
            return StyleNames[style];
        }

        /// <summary>皮肤选择面板里的第二行小字：说清这套配色是什么感觉，光看名字分不出来。</summary>
        public static readonly string[] StyleDesc =
        {
            "中灰底 · 低对比，久看不刺眼",
            "冷蓝灰 · 类 VS Code 深色",
            "炭褐底 · 偏暖，夜里不泛蓝",
            "深蓝黑 · 薄荷绿撞色",
            "深蓝底 · 默认",
            "GitHub Primer · 绿按钮"
        };

        /// <summary>一套皮肤的代表色，只用于画缩略图。</summary>
        public struct SkinSwatch
        {
            public Color Bg, Surface, Line, Accent, Ink;
        }

        private static readonly System.Collections.Generic.Dictionary<string, SkinSwatch> _swCache =
            new System.Collections.Generic.Dictionary<string, SkinSwatch>();

        /// <summary>
        /// 取某套皮肤的代表色，取完把调色板原样还回去（界面不受影响）。
        /// 直接复用 SetNight 的结果，避免缩略图另抄一份颜色、以后改色对不上。
        /// </summary>
        public static SkinSwatch SwatchOf(int style, bool dark)
        {
            string key = style + (dark ? "d" : "l");
            SkinSwatch cached;
            if (_swCache.TryGetValue(key, out cached)) return cached;

            var t = typeof(T);
            var fs = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var saved = new System.Collections.Generic.Dictionary<System.Reflection.FieldInfo, object>();
            foreach (var f in fs)
                if (f.FieldType == typeof(Color)) saved[f] = f.GetValue(null);

            bool oldNight = Night;
            int oldStyle = NightStyle;
            SkinSwatch sw;
            try
            {
                NightStyle = style;
                SetNight(dark);
                sw = new SkinSwatch { Bg = Bg, Surface = Surface, Line = Line, Accent = Jade, Ink = JadeInk };
            }
            finally
            {
                foreach (var kv in saved) kv.Key.SetValue(null, kv.Value);
                Night = oldNight;
                NightStyle = oldStyle;
            }
            _swCache[key] = sw;
            return sw;
        }

        /// <summary>在基础夜色调色板之上叠加一套"更护眼"的取值。</summary>
        public static void ApplyNightStyle(int style)
        {
            if (!Night) return;
            NightStyle = style < 0 ? 0 : (style > 5 ? 4 : style);
            if (NightStyle == 5) { ApplyGithub(true); return; }
            switch (NightStyle)
            {
                case 4: // 用户指定：右侧底 #031422 / 左侧与卡片 #0F172A / 悬停 #1E293B
                    Bg = Color.FromArgb(0x03, 0x14, 0x22); BgTop = Color.FromArgb(0x05, 0x18, 0x28); Bg2 = Color.FromArgb(0x04, 0x16, 0x26);
                    Surface = Color.FromArgb(0x0F, 0x17, 0x2A); SurfaceHi = Color.FromArgb(0x1E, 0x29, 0x3B);
                    Line = Color.FromArgb(0x1E, 0x29, 0x3B); LineSoft = Color.FromArgb(0x16, 0x21, 0x36);
                    RailTop = Color.FromArgb(0x0F, 0x17, 0x2A); RailSel = Color.FromArgb(0x1E, 0x29, 0x3B);
                    Panel2 = Color.FromArgb(0x12, 0x1C, 0x30); Panel3 = Color.FromArgb(0x1E, 0x29, 0x3B); Panel4 = Color.FromArgb(0x15, 0x1F, 0x33);
                    Field = Color.FromArgb(0x16, 0x21, 0x36); Console = Color.FromArgb(0x06, 0x11, 0x1E);
                    HoverSoft = Color.FromArgb(0x1E, 0x29, 0x3B); HoverSoftHi = Color.FromArgb(0x28, 0x36, 0x4B); HoverSoft2 = Color.FromArgb(0x26, 0x33, 0x47);
                    BtnHover = Color.FromArgb(0x22, 0x2F, 0x44); BtnHover2 = Color.FromArgb(0x20, 0x2C, 0x40);
                    Track = Color.FromArgb(0x24, 0x32, 0x47); LineHi = Color.FromArgb(0x2E, 0x3E, 0x56); Line2 = Color.FromArgb(0x22, 0x2F, 0x44); Line3 = Color.FromArgb(0x22, 0x2F, 0x44);
                    Text = Color.FromArgb(0xE8, 0xEF, 0xF6); Text2 = Color.FromArgb(0x9A, 0xAA, 0xBD); Text3 = Color.FromArgb(0x64, 0x74, 0x8B); TextSoft = Color.FromArgb(0xCB, 0xD6, 0xE2);
                    Jade = Color.FromArgb(0x3D, 0xDC, 0x97); JadeDim = Color.FromArgb(0x22, 0x8B, 0x62); JadeHi = Color.FromArgb(0x6B, 0xF0, 0xB6); JadeMid = Color.FromArgb(0x2C, 0xC0, 0x88);
                    JadeLight = Color.FromArgb(0xA6, 0xF7, 0xD2); Jade2 = Color.FromArgb(0x35, 0xCE, 0x8D); JadeDeep = Color.FromArgb(0x1A, 0x7A, 0x57);
                    JadeInk = Color.FromArgb(0x04, 0x22, 0x1A); JadeInk2 = Color.FromArgb(0x05, 0x27, 0x1D);
                    Gold = Color.FromArgb(0xE3, 0xB7, 0x78); Warn = Color.FromArgb(0xF0, 0xA3, 0x5E); Danger = Color.FromArgb(0xF4, 0x69, 0x7F); Blue = Color.FromArgb(0x5A, 0xA0, 0xFF);
                    break;
                case 3: // 深海蓝：接近 EV-AI 控制台那种深蓝黑 + 亮薄荷绿的撞色
                    Bg = Color.FromArgb(0x0B, 0x11, 0x19); BgTop = Color.FromArgb(0x0E, 0x15, 0x1E); Bg2 = Color.FromArgb(0x10, 0x18, 0x22);
                    Surface = Color.FromArgb(0x12, 0x1B, 0x26); SurfaceHi = Color.FromArgb(0x17, 0x22, 0x2F);
                    Line = Color.FromArgb(0x22, 0x30, 0x3F); LineSoft = Color.FromArgb(0x1A, 0x26, 0x34);
                    RailTop = Color.FromArgb(0x0A, 0x10, 0x17); RailSel = Color.FromArgb(0x16, 0x22, 0x2F);
                    Panel2 = Color.FromArgb(0x10, 0x18, 0x23); Panel3 = Color.FromArgb(0x14, 0x1E, 0x2A); Panel4 = Color.FromArgb(0x12, 0x1B, 0x25);
                    Field = Color.FromArgb(0x14, 0x1E, 0x2A); Console = Color.FromArgb(0x0B, 0x12, 0x19);
                    HoverSoft = Color.FromArgb(0x16, 0x20, 0x2C); HoverSoftHi = Color.FromArgb(0x1E, 0x2B, 0x39); HoverSoft2 = Color.FromArgb(0x1E, 0x2A, 0x38);
                    BtnHover = Color.FromArgb(0x1A, 0x26, 0x34); BtnHover2 = Color.FromArgb(0x19, 0x25, 0x32);
                    Track = Color.FromArgb(0x22, 0x30, 0x3F); LineHi = Color.FromArgb(0x2C, 0x3C, 0x4E); Line2 = Color.FromArgb(0x26, 0x34, 0x44); Line3 = Color.FromArgb(0x26, 0x34, 0x44);
                    Text = Color.FromArgb(0xE6, 0xED, 0xF3); Text2 = Color.FromArgb(0x94, 0xA3, 0xB4); Text3 = Color.FromArgb(0x6B, 0x7A, 0x8C); TextSoft = Color.FromArgb(0xCA, 0xD5, 0xDF);
                    Jade = Color.FromArgb(0x3D, 0xDC, 0x97); JadeDim = Color.FromArgb(0x22, 0x8B, 0x62); JadeHi = Color.FromArgb(0x6B, 0xF0, 0xB6); JadeMid = Color.FromArgb(0x2C, 0xC0, 0x88);
                    JadeLight = Color.FromArgb(0xA6, 0xF7, 0xD2); Jade2 = Color.FromArgb(0x35, 0xCE, 0x8D); JadeDeep = Color.FromArgb(0x1A, 0x7A, 0x57);
                    JadeInk = Color.FromArgb(0x05, 0x23, 0x1A); JadeInk2 = Color.FromArgb(0x06, 0x28, 0x1E);
                    Gold = Color.FromArgb(0xE3, 0xB7, 0x78); Warn = Color.FromArgb(0xF0, 0xA3, 0x5E); Danger = Color.FromArgb(0xF4, 0x69, 0x7F); Blue = Color.FromArgb(0x5A, 0xA0, 0xFF);
                    break;
                case 1: // 墨蓝夜：冷蓝灰底，接近 VS Code 深色，最常规
                    Bg = Color.FromArgb(0x11, 0x15, 0x1C); BgTop = Color.FromArgb(0x16, 0x1B, 0x23); Bg2 = Color.FromArgb(0x1A, 0x1F, 0x27);
                    Surface = Color.FromArgb(0x1B, 0x21, 0x29); SurfaceHi = Color.FromArgb(0x23, 0x2A, 0x33);
                    Line = Color.FromArgb(0x2E, 0x37, 0x42); LineSoft = Color.FromArgb(0x24, 0x2C, 0x36);
                    RailTop = Color.FromArgb(0x18, 0x1E, 0x26); RailSel = Color.FromArgb(0x24, 0x2C, 0x35);
                    Panel2 = Color.FromArgb(0x18, 0x1E, 0x25); Panel3 = Color.FromArgb(0x1E, 0x24, 0x2C); Panel4 = Color.FromArgb(0x1B, 0x21, 0x28);
                    Field = Color.FromArgb(0x1F, 0x26, 0x2E); Console = Color.FromArgb(0x15, 0x1A, 0x21);
                    HoverSoft = Color.FromArgb(0x20, 0x27, 0x30); HoverSoftHi = Color.FromArgb(0x2C, 0x35, 0x40); HoverSoft2 = Color.FromArgb(0x2B, 0x33, 0x3D);
                    BtnHover = Color.FromArgb(0x28, 0x30, 0x39); BtnHover2 = Color.FromArgb(0x27, 0x2F, 0x38);
                    Track = Color.FromArgb(0x2E, 0x36, 0x41); LineHi = Color.FromArgb(0x3B, 0x45, 0x51); Line2 = Color.FromArgb(0x33, 0x3C, 0x47); Line3 = Color.FromArgb(0x33, 0x3C, 0x47);
                    Text = Color.FromArgb(0xDC, 0xE3, 0xEC); Text2 = Color.FromArgb(0x9C, 0xA8, 0xB6); Text3 = Color.FromArgb(0x6B, 0x77, 0x85); TextSoft = Color.FromArgb(0xC0, 0xC9, 0xD4);
                    Jade = Color.FromArgb(0x54, 0xC2, 0xA2); JadeDim = Color.FromArgb(0x33, 0x7C, 0x69); JadeHi = Color.FromArgb(0x76, 0xD8, 0xBA); JadeMid = Color.FromArgb(0x44, 0xAE, 0x90);
                    JadeLight = Color.FromArgb(0x9C, 0xE8, 0xCF); Jade2 = Color.FromArgb(0x4F, 0xC0, 0xA0); JadeDeep = Color.FromArgb(0x2A, 0x76, 0x63);
                    JadeInk = Color.FromArgb(0x08, 0x1C, 0x18); JadeInk2 = Color.FromArgb(0x09, 0x22, 0x1D);
                    Gold = Color.FromArgb(0xC9, 0xA6, 0x63); Warn = Color.FromArgb(0xD3, 0x92, 0x5A); Danger = Color.FromArgb(0xD0, 0x6A, 0x66); Blue = Color.FromArgb(0x69, 0x9C, 0xCE);
                    break;
                case 2: // 暖夜：微暖的炭褐底，长时间看不刺眼
                    Bg = Color.FromArgb(0x1A, 0x17, 0x14); BgTop = Color.FromArgb(0x20, 0x1C, 0x18); Bg2 = Color.FromArgb(0x23, 0x1F, 0x1A);
                    Surface = Color.FromArgb(0x26, 0x22, 0x1E); SurfaceHi = Color.FromArgb(0x2E, 0x29, 0x24);
                    Line = Color.FromArgb(0x3B, 0x35, 0x2E); LineSoft = Color.FromArgb(0x2F, 0x2A, 0x24);
                    RailTop = Color.FromArgb(0x22, 0x1E, 0x1A); RailSel = Color.FromArgb(0x2E, 0x29, 0x23);
                    Panel2 = Color.FromArgb(0x22, 0x1E, 0x1A); Panel3 = Color.FromArgb(0x29, 0x24, 0x1F); Panel4 = Color.FromArgb(0x25, 0x21, 0x1C);
                    Field = Color.FromArgb(0x2B, 0x26, 0x21); Console = Color.FromArgb(0x20, 0x1C, 0x18);
                    HoverSoft = Color.FromArgb(0x2C, 0x27, 0x22); HoverSoftHi = Color.FromArgb(0x3A, 0x34, 0x2D); HoverSoft2 = Color.FromArgb(0x38, 0x33, 0x2C);
                    BtnHover = Color.FromArgb(0x35, 0x30, 0x29); BtnHover2 = Color.FromArgb(0x33, 0x2E, 0x28);
                    Track = Color.FromArgb(0x3B, 0x35, 0x2E); LineHi = Color.FromArgb(0x4A, 0x43, 0x39); Line2 = Color.FromArgb(0x41, 0x3B, 0x33); Line3 = Color.FromArgb(0x41, 0x3B, 0x33);
                    Text = Color.FromArgb(0xE3, 0xDE, 0xD5); Text2 = Color.FromArgb(0xA9, 0xA1, 0x96); Text3 = Color.FromArgb(0x76, 0x6E, 0x63); TextSoft = Color.FromArgb(0xCB, 0xC4, 0xB8);
                    Jade = Color.FromArgb(0x7C, 0xC4, 0x9E); JadeDim = Color.FromArgb(0x44, 0x74, 0x5D); JadeHi = Color.FromArgb(0x98, 0xDB, 0xB6); JadeMid = Color.FromArgb(0x64, 0xAE, 0x88);
                    JadeLight = Color.FromArgb(0xB6, 0xEC, 0xCE); Jade2 = Color.FromArgb(0x70, 0xBC, 0x96); JadeDeep = Color.FromArgb(0x39, 0x71, 0x5A);
                    JadeInk = Color.FromArgb(0x14, 0x1B, 0x14); JadeInk2 = Color.FromArgb(0x17, 0x20, 0x18);
                    Gold = Color.FromArgb(0xE0, 0xBE, 0x7E); Warn = Color.FromArgb(0xD9, 0x9A, 0x62); Danger = Color.FromArgb(0xCE, 0x72, 0x68); Blue = Color.FromArgb(0x76, 0xA6, 0xC8);
                    break;
                default: // 0 石墨灰：把纯黑抬成中灰，白字压到浅灰，整体对比降一档
                    Bg = Color.FromArgb(0x16, 0x19, 0x1E); BgTop = Color.FromArgb(0x1B, 0x1F, 0x25); Bg2 = Color.FromArgb(0x1D, 0x22, 0x29);
                    Surface = Color.FromArgb(0x23, 0x28, 0x30); SurfaceHi = Color.FromArgb(0x2A, 0x30, 0x38);
                    Line = Color.FromArgb(0x35, 0x3C, 0x46); LineSoft = Color.FromArgb(0x2A, 0x31, 0x3A);
                    RailTop = Color.FromArgb(0x1E, 0x23, 0x2A); RailSel = Color.FromArgb(0x2A, 0x31, 0x3A);
                    Panel2 = Color.FromArgb(0x1E, 0x23, 0x2A); Panel3 = Color.FromArgb(0x24, 0x2A, 0x32); Panel4 = Color.FromArgb(0x21, 0x27, 0x2F);
                    Field = Color.FromArgb(0x25, 0x2B, 0x33); Console = Color.FromArgb(0x1A, 0x1F, 0x25);
                    HoverSoft = Color.FromArgb(0x25, 0x2B, 0x34); HoverSoftHi = Color.FromArgb(0x33, 0x3B, 0x45); HoverSoft2 = Color.FromArgb(0x32, 0x39, 0x44);
                    BtnHover = Color.FromArgb(0x2F, 0x37, 0x41); BtnHover2 = Color.FromArgb(0x2E, 0x36, 0x40);
                    Track = Color.FromArgb(0x36, 0x3E, 0x49); LineHi = Color.FromArgb(0x42, 0x4B, 0x57); Line2 = Color.FromArgb(0x3A, 0x43, 0x4E); Line3 = Color.FromArgb(0x3A, 0x43, 0x4E);
                    Text = Color.FromArgb(0xDD, 0xE2, 0xE9); Text2 = Color.FromArgb(0xA3, 0xAC, 0xB8); Text3 = Color.FromArgb(0x6F, 0x78, 0x85); TextSoft = Color.FromArgb(0xC2, 0xC9, 0xD3);
                    Jade = Color.FromArgb(0x5F, 0xCB, 0xA8); JadeDim = Color.FromArgb(0x36, 0x7F, 0x6A); JadeHi = Color.FromArgb(0x82, 0xDD, 0xC0); JadeMid = Color.FromArgb(0x4B, 0xB4, 0x95);
                    JadeLight = Color.FromArgb(0xA2, 0xEB, 0xD3); Jade2 = Color.FromArgb(0x57, 0xC3, 0xA1); JadeDeep = Color.FromArgb(0x2C, 0x78, 0x64);
                    JadeInk = Color.FromArgb(0x0A, 0x1E, 0x19); JadeInk2 = Color.FromArgb(0x0B, 0x24, 0x1E);
                    Gold = Color.FromArgb(0xD3, 0xAE, 0x6C); Warn = Color.FromArgb(0xDD, 0x9A, 0x60); Danger = Color.FromArgb(0xD5, 0x6E, 0x6A); Blue = Color.FromArgb(0x6E, 0xA2, 0xD2);
                    break;
            }
        }

        /// <summary>
        /// GitHub 皮肤：整张调色板照 Primer 的取值抄（Canvas / Border / fg / success / accent / danger）。
        /// 深色是 #0D1117 画布 + #161B22 面层 + #30363D 边框；浅色是白画布 + #F6F8FA 面层 + #D0D7DE 边框。
        /// 主按钮用 GitHub 那种绿（#238636 深色 / #1F883D 浅色），链接蓝放在 T.Blue 上。
        /// </summary>
        public static void ApplyGithub(bool dark)
        {
            if (dark)
            {
                Bg = Color.FromArgb(0x0D, 0x11, 0x17); BgTop = Color.FromArgb(0x0D, 0x11, 0x17); Bg2 = Color.FromArgb(0x16, 0x1B, 0x22);
                Surface = Color.FromArgb(0x16, 0x1B, 0x22); SurfaceHi = Color.FromArgb(0x1C, 0x21, 0x28);
                Line = Color.FromArgb(0x30, 0x36, 0x3D); LineSoft = Color.FromArgb(0x21, 0x26, 0x2D);
                RailTop = Color.FromArgb(0x01, 0x04, 0x09); RailSel = Color.FromArgb(0x16, 0x1B, 0x22);
                Panel2 = Color.FromArgb(0x0D, 0x11, 0x17); Panel3 = Color.FromArgb(0x16, 0x1B, 0x22); Panel4 = Color.FromArgb(0x12, 0x17, 0x1E);
                Field = Color.FromArgb(0x0D, 0x11, 0x17); Console = Color.FromArgb(0x01, 0x04, 0x09);
                HoverSoft = Color.FromArgb(0x1C, 0x21, 0x28); HoverSoftHi = Color.FromArgb(0x26, 0x2C, 0x36); HoverSoft2 = Color.FromArgb(0x21, 0x26, 0x2D);
                BtnHover = Color.FromArgb(0x26, 0x2C, 0x36); BtnHover2 = Color.FromArgb(0x21, 0x26, 0x2D);
                Track = Color.FromArgb(0x30, 0x36, 0x3D); LineHi = Color.FromArgb(0x3D, 0x44, 0x4D); Line2 = Color.FromArgb(0x30, 0x36, 0x3D); Line3 = Color.FromArgb(0x30, 0x36, 0x3D);
                Text = Color.FromArgb(0xE6, 0xED, 0xF3); Text2 = Color.FromArgb(0x8B, 0x94, 0x9E); Text3 = Color.FromArgb(0x6E, 0x76, 0x81); TextSoft = Color.FromArgb(0xC9, 0xD1, 0xD9);
                // 绿：Jade 是强调色（成功/已连接），JadeHi→JadeMid 是主按钮渐变，白字压在 #238636 上
                Jade = Color.FromArgb(0x3F, 0xB9, 0x50); JadeDim = Color.FromArgb(0x23, 0x86, 0x36); JadeHi = Color.FromArgb(0x2E, 0xA0, 0x43); JadeMid = Color.FromArgb(0x23, 0x86, 0x36);
                JadeLight = Color.FromArgb(0x56, 0xD3, 0x64); Jade2 = Color.FromArgb(0x3F, 0xB9, 0x50); JadeDeep = Color.FromArgb(0x1A, 0x7F, 0x37);
                JadeInk = Color.FromArgb(0xFF, 0xFF, 0xFF); JadeInk2 = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Gold = Color.FromArgb(0xD2, 0x99, 0x22); Warn = Color.FromArgb(0xD2, 0x99, 0x22); Danger = Color.FromArgb(0xF8, 0x51, 0x49); Blue = Color.FromArgb(0x58, 0xA6, 0xFF);
            }
            else
            {
                Bg = Color.FromArgb(0xFF, 0xFF, 0xFF); BgTop = Color.FromArgb(0xFF, 0xFF, 0xFF); Bg2 = Color.FromArgb(0xF6, 0xF8, 0xFA);
                Surface = Color.FromArgb(0xFF, 0xFF, 0xFF); SurfaceHi = Color.FromArgb(0xF6, 0xF8, 0xFA);
                Line = Color.FromArgb(0xD0, 0xD7, 0xDE); LineSoft = Color.FromArgb(0xD8, 0xDE, 0xE4);
                RailTop = Color.FromArgb(0xF6, 0xF8, 0xFA); RailSel = Color.FromArgb(0xEA, 0xEE, 0xF2);
                Panel2 = Color.FromArgb(0xF6, 0xF8, 0xFA); Panel3 = Color.FromArgb(0xFF, 0xFF, 0xFF); Panel4 = Color.FromArgb(0xF6, 0xF8, 0xFA);
                Field = Color.FromArgb(0xFF, 0xFF, 0xFF); Console = Color.FromArgb(0xF6, 0xF8, 0xFA);
                HoverSoft = Color.FromArgb(0xF3, 0xF4, 0xF6); HoverSoftHi = Color.FromArgb(0xEA, 0xEE, 0xF2); HoverSoft2 = Color.FromArgb(0xEA, 0xEE, 0xF2);
                BtnHover = Color.FromArgb(0xF3, 0xF4, 0xF6); BtnHover2 = Color.FromArgb(0xEA, 0xEE, 0xF2);
                Track = Color.FromArgb(0xD0, 0xD7, 0xDE); LineHi = Color.FromArgb(0xAF, 0xB8, 0xC1); Line2 = Color.FromArgb(0xD8, 0xDE, 0xE4); Line3 = Color.FromArgb(0xD0, 0xD7, 0xDE);
                Text = Color.FromArgb(0x1F, 0x23, 0x28); Text2 = Color.FromArgb(0x65, 0x6D, 0x76); Text3 = Color.FromArgb(0x8C, 0x95, 0x9F); TextSoft = Color.FromArgb(0x42, 0x4A, 0x53);
                Jade = Color.FromArgb(0x1A, 0x7F, 0x37); JadeDim = Color.FromArgb(0x4A, 0xC2, 0x6B); JadeHi = Color.FromArgb(0x2D, 0xA4, 0x4E); JadeMid = Color.FromArgb(0x1F, 0x88, 0x3D);
                JadeLight = Color.FromArgb(0x1A, 0x7F, 0x37); Jade2 = Color.FromArgb(0x2D, 0xA4, 0x4E); JadeDeep = Color.FromArgb(0x0F, 0x53, 0x23);
                JadeInk = Color.FromArgb(0xFF, 0xFF, 0xFF); JadeInk2 = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Gold = Color.FromArgb(0x9A, 0x67, 0x00); Warn = Color.FromArgb(0x9A, 0x67, 0x00); Danger = Color.FromArgb(0xCF, 0x22, 0x2E); Blue = Color.FromArgb(0x09, 0x69, 0xDA);
            }
        }

        public static FontFamily FfUI = LoadFamily("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI");
        public static FontFamily FfNum = LoadFamily("Segoe UI", "Microsoft YaHei UI");
        public static FontFamily FfMono = LoadFamily("Cascadia Mono", "Consolas", "Courier New");

        /// <summary>DPI 缩放系数（96dpi = 1.0）。所有布局用逻辑像素，绘制时统一缩放。</summary>
        public static float S = 1f;

        public static Point LP(Point p) { return new Point((int)(p.X / S), (int)(p.Y / S)); }
        public static float LS(int v) { return v / S; }
        public static int RS(float v) { return (int)Math.Round(v * S); }
        public static RectangleF Scaled(Graphics g, Control c) { return new RectangleF(0, 0, c.Width / S, c.Height / S); }

        private static FontFamily LoadFamily(params string[] names)
        {
            foreach (var n in names)
            {
                try { return new FontFamily(n); }
                catch { }
            }
            return FontFamily.GenericSansSerif;
        }

        public static Font F(float size, FontStyle style = FontStyle.Regular) { return new Font(FfUI, size, style, GraphicsUnit.Pixel); }
        public static Font Num(float size, FontStyle style = FontStyle.Regular) { return new Font(FfNum, size, style, GraphicsUnit.Pixel); }
        public static Font Mono(float size, FontStyle style = FontStyle.Regular) { return new Font(FfMono, size, style, GraphicsUnit.Pixel); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color c)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Brush b)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius)) g.FillPath(b, path);
            g.SmoothingMode = old;
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color c, float w = 1f)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            var rr = new RectangleF(r.X + w / 2, r.Y + w / 2, Math.Max(1, r.Width - w), Math.Max(1, r.Height - w));
            using (var path = Round(rr, radius))
            using (var pen = new Pen(c, w)) g.DrawPath(pen, path);
            g.SmoothingMode = old;
        }

        /// <summary>品牌标志：近黑圆角底板 + 余烬橙纸飞机。与 assets\app.ico 同一套几何。</summary>
        public static void Mark(Graphics g, RectangleF box)
        {
            float s = Math.Min(box.Width, box.Height);
            var r = new RectangleF(box.X + (box.Width - s) / 2f, box.Y + (box.Height - s) / 2f, s, s);
            float rad = s * 0.235f;
            var cTop = Color.FromArgb(0x22, 0x29, 0x4A);
            var cMid = Color.FromArgb(0x11, 0x13, 0x21);
            var cBot = Color.FromArgb(0x07, 0x08, 0x0E);

            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var plate = Round(r, rad))
            {
                using (var br = new LinearGradientBrush(r, cTop, cBot, 90f))
                {
                    var cb = new ColorBlend(3);
                    cb.Colors = new[] { cTop, cMid, cBot };
                    cb.Positions = new[] { 0f, 0.55f, 1f };
                    br.InterpolationColors = cb;
                    g.FillPath(br, plate);
                }

                var st = g.Save();
                g.SetClip(plate, CombineMode.Intersect);

                using (var sh = new LinearGradientBrush(new RectangleF(r.X, r.Y, s, s * 0.34f),
                           Color.FromArgb(38, 0xA0, 0xC4, 0xFF), Color.FromArgb(0, 0xA0, 0xC4, 0xFF), 90f))
                    g.FillRectangle(sh, r.X, r.Y, s, s * 0.34f);

                if (s >= 18f)
                    using (var gp = new GraphicsPath())
                    {
                        float cx = r.X + s * 0.50f, cy = r.Y + s * 0.52f, rad2 = s * 0.46f;
                        gp.AddEllipse(cx - rad2, cy - rad2, rad2 * 2, rad2 * 2);
                        using (var pgb = new PathGradientBrush(gp))
                        {
                            pgb.CenterPoint = new PointF(cx, cy);
                            pgb.CenterColor = Color.FromArgb(34, 0xFF, 0x8C, 0x3C);
                            pgb.SurroundColors = new[] { Color.FromArgb(0, 0xFF, 0x8C, 0x3C) };
                            g.FillPath(pgb, gp);
                        }
                    }

                if (s >= 16f)
                {
                    float[][] segs = new float[][] {
                        new float[] { 0.360f, 0.300f, 0.150f },
                        new float[] { 0.462f, 0.424f, 0.118f },
                        new float[] { 0.560f, 0.552f, 0.078f }
                    };
                    foreach (var seg in segs)
                    {
                        using (var pen = new Pen(Color.FromArgb(206, 0xFF, 0xB0, 0x68), Math.Max(0.9f, s * 0.022f)))
                        {
                            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                            float x1 = r.X + s * 0.085f;
                            g.DrawLine(pen, x1, r.Y + s * seg[0], x1 + s * seg[2], r.Y + s * seg[1]);
                        }
                    }
                }

                var nose = new PointF(r.X + s * 0.885f, r.Y + s * 0.145f);
                var tailU = new PointF(r.X + s * 0.100f, r.Y + s * 0.435f);
                var fold = new PointF(r.X + s * 0.545f, r.Y + s * 0.645f);
                using (var plane = new GraphicsPath())
                {
                    plane.AddPolygon(new[] { nose, tailU, fold });
                    var pb = plane.GetBounds();
                    g.SetClip(plane, CombineMode.Intersect);
                    using (var wb = new LinearGradientBrush(pb, Color.FromArgb(0xFF, 0xFF, 0xF0, 0xBE), Color.FromArgb(0xF4, 0x58, 0x54), 62f))
                    {
                        var cb2 = new ColorBlend(3);
                        cb2.Colors = new[] { Color.FromArgb(0xFF, 0xFF, 0xF0, 0xBE), Color.FromArgb(0xFF, 0xA8, 0x54), Color.FromArgb(0xF4, 0x58, 0x54) };
                        cb2.Positions = new[] { 0f, 0.52f, 1f };
                        wb.InterpolationColors = cb2;
                        g.FillRectangle(wb, pb);
                    }
                }

                g.Restore(st);

                using (var hair = new Pen(Color.FromArgb(140, 0x4A, 0x5A, 0x8A), Math.Max(0.7f, s * 0.009f)))
                    g.DrawPath(hair, plate);
            }
            g.SmoothingMode = old;
        }

        /// <summary>卡片：面层 + 1px 细边，悬停时抬一档。</summary>
        public static void Card(Graphics g, RectangleF r, float radius = 14f, bool hover = false)
        {
            var c = hover ? SurfaceHi : Surface;
            FillRound(g, r, radius, c);
            StrokeRound(g, r, radius, hover ? LineHi : Line, 1f);
        }

        public static void Draw(Graphics g, string s, Font f, Color c, float x, float y)
        {
            var old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
            g.TextRenderingHint = old;
        }

        public static void TextR(Graphics g, string s, Font f, Color c, float right, float y)
        {
            var sz = g.MeasureString(s, f);
            Draw(g, s, f, c, right - sz.Width, y);
        }

        public static void TextC(Graphics g, string s, Font f, Color c, RectangleF box)
        {
            var sz = g.MeasureString(s, f);
            Draw(g, s, f, c, box.X + (box.Width - sz.Width) / 2f, box.Y + (box.Height - sz.Height) / 2f);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        /// <summary>玉色主按钮的渐变。</summary>
        public static LinearGradientBrush JadeBrush(RectangleF r)
        {
            return new LinearGradientBrush(r, JadeHi, JadeMid, 35f);
        }

        public static void Hairline(Graphics g, float x1, float x2, float y)
        {
            using (var p = new Pen(LineSoft, 1f)) g.DrawLine(p, x1, y, x2, y);
        }

        public static void Dot(Graphics g, float cx, float cy, float r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        public static void Ring(Graphics g, RectangleF box, float thickness, float pct, Color track, Color fill)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(track, thickness)) g.DrawEllipse(p, box);
            if (pct > 0)
            {
                using (var p = new Pen(fill, thickness))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, box, -90, Math.Max(1f, 360f * Math.Min(1f, pct)));
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }
    }
}
