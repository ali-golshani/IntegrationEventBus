namespace IntegrationEventBus.SqlServer;

internal interface ISqlScriptProvider
{
    string Get(SqlScript script);
}
