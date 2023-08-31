# Simple SQL Change Notifications

Designed to run as a scheduled task running as a user with access to the SQL server.

Can be pointed at any database/query combination and supports composite primary keys.

The configuration can be changed at any time.

If changes are detected the program will email the current runs log file to the configured email address(es).

---

Configuration is via appsettings.json:-
```json
"SimpleSqlChangeNotificationOptions": {
    "ConnectionString": "Server=sql2;Database=erp;Trusted_Connection=True;TrustServerCertificate=True;",
    "Query": "SELECT UniqueID, SupplierTitle, BankAccount, BankSort, EFTReference, Terms FROM SUPPLIERS ORDER BY SUPPLIERTITLE",
    "PrimaryKey": [
    "UniqueID"
    ],
    "SmtpServer": "smtp.domain.local",
    "SmtpPort": 25,
    "SmtpFromAddress": "noreply@stewartcelani.com",
    "SmtpToAddress": [
    "simplesqlchangenotifications@stewartcelani.com.au",
    "extraaddresseshere@example.com"
    ],
    "SmtpSSL": false,
    "SmtpUsername": null,
    "SmtpPassword": null
}
```

Installation:-
- Download the latest release, unzip and run from its own directory. If you need to monitor multiple queries they each need their own root folder.

Pros:-
- Set and forget once you have configured a scheduled task.
- Don't need to use a database trigger, especially in situations where you can't (e.g a third party database or when you don't have access to the SQL server).
- Can monitor any SELECT query with little setup.
- Can change the query at any time by editing the appsettings.json.

Cons:-
- Query must be a SELECT query.
- If you want to monitor more than one query you'll have to copy the directory and change the appsettings.json and hook up another scheduled task.
- Stores the results of the last query as a queryCache.json file so keep in mind disk space if you're running extremely large queries (millions of rows).
- Can only compare results of when the program was last run -- not very useful without a scheduled/repeating task.

Issues or feature requests-
- Raise an issue (be as detailed as possible) and I'll try to accommodate.
