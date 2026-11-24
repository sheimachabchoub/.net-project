using Microsoft.AspNetCore.Components;

namespace MediaLib.Web.Components.Shared;

/// <summary>
/// Dessins SVG réutilisés (style « trait d'encre » : contours brun bois, aplats de la palette).
/// Un seul personnage — Archimède, le hibou bibliothécaire — est dessiné ici et utilisé partout.
/// </summary>
public static class Dessins
{
    private const string Encre = "#4A3428";

    /// <summary>Archimède dans une boîte de 120 × 150 (coordonnées locales).</summary>
    public static MarkupString Hibou => new($"""
        <g stroke="{Encre}" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M28 40 L22 14 L44 30" fill="#E8D7BA"/>
          <path d="M92 40 L98 14 L76 30" fill="#E8D7BA"/>
          <path d="M60 24c-26 0-40 16-40 40v34c0 24 18 40 40 40s40-16 40-40V64c0-24-14-40-40-40z" fill="#E8D7BA"/>
          <path d="M36 84c4 18 12 34 24 42 12-8 20-24 24-42-8 4-16 6-24 6s-16-2-24-6z" fill="#788363"/>
          <path d="M60 90v34" stroke-dasharray="1 5" stroke-width="2"/>
          <circle cx="60" cy="98" r="1.8" fill="#C39A5A" stroke="none"/>
          <circle cx="60" cy="110" r="1.8" fill="#C39A5A" stroke="none"/>
          <path d="M30 96c-8 8-10 22-4 34M90 96c8 8 10 22 4 34" fill="none" stroke-width="2"/>
          <path d="M34 52c4-10 14-14 26-12 12-2 22 2 26 12-2 14-12 22-26 22S36 66 34 52z" fill="#F5EBDD"/>
          <circle cx="46" cy="56" r="10" fill="#FBF6EE" stroke="#C39A5A" stroke-width="2.6"/>
          <circle cx="74" cy="56" r="10" fill="#FBF6EE" stroke="#C39A5A" stroke-width="2.6"/>
          <path d="M56 55h8" stroke="#C39A5A" stroke-width="2.4"/>
          <circle cx="46" cy="57" r="3.6" fill="{Encre}" stroke="none"/>
          <circle cx="74" cy="57" r="3.6" fill="{Encre}" stroke="none"/>
          <circle cx="47.4" cy="55.6" r="1" fill="#FBF6EE" stroke="none"/>
          <circle cx="75.4" cy="55.6" r="1" fill="#FBF6EE" stroke="none"/>
          <path d="M57 64l3 6 3-6z" fill="#A7614C" stroke-width="1.6"/>
          <path d="M48 138v6M54 138v6M66 138v6M72 138v6" stroke="#A7614C" stroke-width="3"/>
          <path d="M40 120h40l-3 14H43z" fill="#A7614C" stroke-width="2"/>
          <path d="M60 120v14" stroke-width="1.4"/>
        </g>
        """);

    /// <summary>Lampe de banquier avec son halo (boîte 60 × 90, halo centré sur l'abat-jour).</summary>
    public static MarkupString Lampe(string classeHalo = "halo") => new($"""
        <circle class="{classeHalo}" cx="30" cy="28" r="34" fill="url(#halo-lampe)"/>
        <g stroke="{Encre}" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M8 34h44l-8-16H16z" fill="#788363"/>
          <path d="M30 34v40" stroke-width="4" stroke="#C39A5A"/>
          <path d="M30 34v40" fill="none"/>
          <path d="M14 82c0-6 7-8 16-8s16 2 16 8z" fill="#C39A5A"/>
        </g>
        """);

    public static MarkupString DefinitionsHalo => new("""
        <defs>
          <radialGradient id="halo-lampe" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stop-color="#F3D9A4" stop-opacity="1"/>
            <stop offset="100%" stop-color="#C39A5A" stop-opacity="0"/>
          </radialGradient>
        </defs>
        """);
}
