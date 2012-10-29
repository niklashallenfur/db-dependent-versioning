UPDATE DbVersioningHistory SET ScriptVersion = '{1}' WHERE ScriptVersion = '{0}'
GO
UPDATE DbVersioningHistory SET DependentOnScriptVersion = '{1}' WHERE DependentOnScriptVersion = '{0}'
GO