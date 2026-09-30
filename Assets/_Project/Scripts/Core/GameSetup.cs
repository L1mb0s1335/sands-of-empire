namespace Runeterra.Core
{
    public enum Scenario
    {
        /// <summary>«Исторический 1187»: исторические претензии, стартовое перемирие, войны по претензиям.</summary>
        Historical1187,
        /// <summary>«Мирное развитие»: войны выключены, победа по очкам.</summary>
        PeacefulDevelopment,
    }

    /// <summary>Настройки новой партии из меню старта: сценарий, сторона игрока, длина партии.</summary>
    public class GameSetup
    {
        public Scenario Scenario = Scenario.Historical1187;
        /// <summary>id региона игрока (null — первый регион карты).</summary>
        public string HumanRegion;
        /// <summary>После этого хода партия заканчивается подсчётом очков.</summary>
        public int TurnLimit = 200;

        public static readonly int[] Lengths = { 100, 200, 300 };

        public static string LengthName(int turns) => turns switch
        {
            <= 100 => "Короткая",
            <= 200 => "Обычная",
            _ => "Долгая",
        };

        public static string ScenarioName(Scenario s) => s == Scenario.PeacefulDevelopment ? "Мирное развитие" : "Исторический 1187";

        public static string ScenarioText(Scenario s) => s == Scenario.PeacefulDevelopment
            ? "Войны выключены. Шесть держав соревнуются хозяйством, торговлей и знаниями; победа по очкам в конце партии."
            : "Осень 1187 года. Исторические претензии, стартовое перемирие на 20 ходов, дальше — войны по претензиям, союзы и коалиции. " +
              "Победа — покорить все державы или набрать больше всех очков к концу партии; падение вашей столицы — поражение.";

        /// <summary>Настройки, с которыми сцена партии начнёт новую игру (null — по умолчанию).</summary>
        public static GameSetup Pending { get; set; }
    }
}
