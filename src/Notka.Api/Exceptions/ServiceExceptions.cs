namespace Notka.Api.Exceptions
{
    internal abstract class ServiceException(string message) : Exception(message);

    internal sealed class NotFoundException(string entity, int id)
        : ServiceException($"Nie znaleziono: {entity} (id {id}).");

    internal sealed class BusinessRuleException(string message) : ServiceException(message);
}
