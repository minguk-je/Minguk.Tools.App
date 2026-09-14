using System;
using System.Linq;

using Minguk.Tools.Projects;

namespace Minguk.Tools.Tests;

/// <summary>
/// 옛 자리를 첫 솔루션으로 옮긴다. <c>--apply</c> 를 안 주면 <b>보여 주기만</b> 한다.
/// </summary>
/// <remarks>
/// 사진 수백 장과 손으로 찍은 라벨이 움직이는 명령이라 기본이 미리 보기다. 무엇이 어디로 가는지 읽고
/// 맞으면 <c>--apply</c> 를 붙여 다시 돌린다.
/// </remarks>
public static class MigrateProbe
{
    public static int Run(string[] args)
    {
        var apply = args.Contains("--apply");

        var plan = SolutionMigration.Plan();

        if (plan.IsEmpty)
        {
            Console.WriteLine("옮길 것이 없습니다. 옛 자리에 데이터셋·프레임 저장·스크립트가 없거나 이미 옮겼습니다.");
            return 0;
        }

        Console.WriteLine($"솔루션   {plan.SolutionName}   →  {plan.SolutionFile}");
        Console.WriteLine($"프로젝트 {plan.ProjectName}   →  {plan.ProjectDirectory}");
        Console.WriteLine();

        foreach (var step in plan.Steps)
            Console.WriteLine($"  {step.What}\n      {step.From}\n   →  {step.To}\n");

        if (plan.Junctions.Count > 0)
        {
            Console.WriteLine("  다시 걸 링크(시험 폴더):");

            foreach (var (link, target) in plan.Junctions)
                Console.WriteLine($"      {link}  →  {target}");

            Console.WriteLine();
        }

        if (!apply)
        {
            Console.WriteLine("미리 보기입니다. 실제로 옮기려면 --apply 를 붙여 다시 돌리세요.");
            return 0;
        }

        try
        {
            SolutionMigration.Apply(plan, text => Console.WriteLine($"  {text}"));

            Console.WriteLine();
            Console.WriteLine("옮겼습니다.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"[FAIL] 옮기다 멈췄습니다 - {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine("옮긴 데까지는 그대로 남아 있습니다. 무엇이 어디 있는지 확인하고 다시 돌리세요.");

            return 1;
        }
    }
}
