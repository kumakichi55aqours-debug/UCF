#ifndef STUDIOMARON_BLEND_COMPOSITE_INCLUDED
#define STUDIOMARON_BLEND_COMPOSITE_INCLUDED

// BlendMode.cs の並びと同じ合成式。
// UI-CompositShader と UI-URArtEffect の両方から使う。

half3 RGBtoHSV(half3 rgb)
{
    half r = rgb.r;
    half g = rgb.g;
    half b = rgb.b;

    half maxC = max(r, max(g, b));
    half minC = min(r, min(g, b));
    half delta = maxC - minC;
    // 無彩色だと delta が 0 になり、色相の割り算が NaN になる
    half safeDelta = max(delta, 0.0001);

    half h_r = (g - b) / safeDelta;
    h_r += (h_r < 0.0) ? 6.0 : 0.0;
    half h_g = 2.0 + (b - r) / safeDelta;
    half h_b = 4.0 + (r - g) / safeDelta;

    half h = (maxC == r) ? h_r : ((maxC == g) ? h_g : h_b);
    h = (delta > 0.0001) ? (h / 6.0) : 0.0;
    half s = (maxC > 0.0001) ? (delta / maxC) : 0.0;

    return half3(h, s, maxC);
}

half3 HSVtoRGB(half3 hsv)
{
    half h = hsv.x;
    half s = hsv.y;
    half v = hsv.z;

    half r = v;
    half g = v;
    half b = v;

    h *= 6.0;
    half f = frac(h);
    switch (floor(h))
    {
        default:
        case 0:
            g *= 1 - s * (1 - f);
            b *= 1 - s;
            break;
        case 1:
            r *= 1 - s * f;
            b *= 1 - s;
            break;
        case 2:
            r *= 1 - s;
            b *= 1 - s * (1 - f);
            break;
        case 3:
            r *= 1 - s;
            g *= 1 - s * f;
            break;
        case 4:
            r *= 1 - s * (1 - f);
            g *= 1 - s;
            break;
        case 5:
            g *= 1 - s;
            b *= 1 - s * f;
            break;
    }

    r = (s > 0.0) ? r : v;
    g = (s > 0.0) ? g : v;
    b = (s > 0.0) ? b : v;
    return half3(r, g, b);
}

// a = 下地、b = 上に載せる色、comp = StudioMaron.BlendMode の番号
// 全モードを足し合わせると、使わない除算の NaN が 0 倍でも残って全モードの色が壊れる。
// 選んだ式だけ計算する。
half3 ApplyComposition(half3 a, half3 b, float comp)
{
    int mode = (int)floor(comp + 0.5);

    if (mode == 1)
        return a * b;
    if (mode == 2)
        return a + b;
    if (mode == 3)
        return a / max(b, 0.0001);
    if (mode == 4)
        return 1 - (1 - a) * (1 - b);

    half3 ovlyMul = 2 * a * b;
    half3 ovlyScr = 1 - 2 * (1 - a) * (1 - b);
    if (mode == 5)
        return step(a, 0.5) * ovlyMul + (1 - step(a, 0.5)) * ovlyScr;
    if (mode == 6)
        return step(b, 0.5) * ovlyMul + (1 - step(b, 0.5)) * ovlyScr;
    if (mode == 7)
        return (1 - 2 * b) * a * a + 2 * a * b;
    if (mode == 8)
        return a / max(1 - b, 0.0001);
    if (mode == 9)
        return a + b - half3(1, 1, 1);
    if (mode == 10)
        return abs(a - b);
    if (mode == 11)
        return min(a, b);
    if (mode == 12)
        return max(a, b);

    half3 hsvA = RGBtoHSV(a);
    half3 hsvB = RGBtoHSV(b);
    if (mode == 13)
        return HSVtoRGB(half3(hsvB.x, hsvA.y, hsvA.z));
    if (mode == 14)
        return HSVtoRGB(half3(hsvA.x, hsvB.y, hsvA.z));
    if (mode == 15)
        return HSVtoRGB(half3(hsvB.x, hsvB.y, hsvA.z));
    if (mode == 16)
        return HSVtoRGB(half3(hsvA.x, hsvA.y, hsvB.z));

    return b;
}

#endif
