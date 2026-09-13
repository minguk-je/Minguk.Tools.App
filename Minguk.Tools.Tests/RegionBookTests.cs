using System;
using System.IO;
using System.Windows;

using Minguk.Tools.Vision.Regions;

namespace Minguk.Tools.Tests;

/// <summary>
/// 이름 붙인 자리 목록(<see cref="RegionBook"/>). 저장·되읽기·이름 겹침·망가진 파일.
/// </summary>
/// <remarks>
/// 스크립트가 이름으로 부르므로 <b>이름이 겹치면 어느 쪽을 볼지 알 수 없다</b>. 그리고 파일 하나 때문에
/// 화면이 안 뜨면 안 된다 - 망가져도 빈 목록으로 가야 한다.
/// </remarks>
internal static partial class Program
{
    private static void TestRegionBook()
    {
        var folder = Path.Combine(Path.GetTempPath(), "minguk-regions-" + Guid.NewGuid().ToString("N"));

        try
        {
            var book = new RegionBook(folder);

            book.Put(new NamedRegion { Name = "탄약", Rect = new Rect(0.9, 0.86, 0.09, 0.06) });
            book.Put(new NamedRegion { Name = "체력", Rect = new Rect(0.09, 0.80, 0.11, 0.05), Ink = false });
            book.Save();

            var read = RegionBook.Load(folder);
            var ammo = read.Find("탄약");

            Check("저장했다 되읽으면 자리가 그대로다",
                  read.Regions.Count == 2 && ammo is not null && Near(ammo.Rect.X, 0.9) && Near(ammo.Rect.Width, 0.09) && ammo.Ink,
                  ammo is null ? "탄약을 못 찾음" : ammo.Describe);

            Check("이름은 대소문자를 안 가린다 - 스크립트가 글자 하나 달라 못 찾으면 자리 탓인 줄 안다",
                  read.Find("ammo") is null && read.Find("체력") is not null && read.Find("  탄약 ") is not null,
                  "");

            // 같은 이름을 또 넣으면 덮어쓴다. 겹치면 스크립트가 어느 쪽을 볼지 알 수 없다.
            read.Put(new NamedRegion { Name = "탄약", Rect = new Rect(0.5, 0.5, 0.1, 0.1) });

            Check("같은 이름은 덮어쓴다", read.Regions.Count == 2 && Near(read.Find("탄약")!.Rect.X, 0.5),
                  $"{read.Regions.Count}개");

            Check("지우면 없다", read.Remove("체력") && read.Find("체력") is null && !read.Remove("없는것"), "");

            // 망가진 파일 - 빈 목록으로 가야 한다.
            File.WriteAllText(Path.Combine(folder, RegionBook.FileName), "{ 이건 json 이 아니다");

            Check("파일이 망가져도 터지지 않는다", RegionBook.Load(folder).Regions.Count == 0, "");

            // 이름 없는 것은 못 넣는다 - 스크립트가 부를 길이 없다.
            var refused = false;

            try { new RegionBook(folder).Put(new NamedRegion { Name = "   " }); }
            catch (ArgumentException) { refused = true; }

            Check("이름 없는 자리는 안 받는다", refused, "");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (Exception) { }
        }
    }
}
