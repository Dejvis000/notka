namespace Notka.Api.ErrorHandling
{
    internal static class ApiProblems
    {
        public const string NotFoundTitle = "Nie znaleziono zasobu.";
        public const string BusinessRuleTitle = "Żądanie narusza regułę biznesową.";
        public const string UnexpectedTitle = "Wystąpił nieoczekiwany błąd.";
        public const string UnexpectedDetail = "Szczegóły błędu zapisano w logu aplikacji.";
    }
}
