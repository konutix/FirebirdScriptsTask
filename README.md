# 🗄️ DbMetaTool  
### Firebird 5.0 Metadata Exporter, Builder & Updater (C# / .NET 8)

**DbMetaTool** is a console application built in **.NET 8.0** designed to:

1. **Build a new Firebird database** from exported metadata scripts  
2. **Export metadata** (domains, tables, procedures) from an existing Firebird 5.0 database  
3. **Update an existing database** using exported metadata scripts  

The tool supports a **simplified metadata model**:

- ✔ domains  
- ✔ tables (with columns)  
- ✔ stored procedures  

---

## ✨ Features

### 🔨 Build Database (`build-db`)
Creates a **new Firebird 5.0 database** using metadata scripts (`*.sql`).

### 📤 Export Scripts (`export-scripts`)
Exports metadata from an existing Firebird 5.0 database into:

- `domains.sql`
- `tables.sql`
- `procedures.sql`

### 🔁 Update Database (`update-db`)
Applies metadata changes to an existing database:

- adds missing domains
- adds missing columns
- recreates or alters stored procedures

---

# 🚀 Getting Started

## ✔ 1. Install .NET 8 SDK

https://dotnet.microsoft.com/en-us/download

## ✔ 2. Download Firebird Embedded

A PowerShell helper script is included:

```powershell
cd DbMetaTool.Tests
./setup-firebird.ps1
```

This downloads the **Firebird 5.0 Embedded** binaries into:

```
/fb5
```

The application uses `fbclient.dll` from this folder during the tests and execution.

---

# ▶️ Usage

Run the console tool from command line:

```
DbMetaTool <command> [options]
```

---

## 🔨 Build Database

```
DbMetaTool build-db --db-dir "C:\db\fb5" --scripts-dir "C:\metadata"
```

This will:

- create a new Firebird `.fdb` file in `--db-dir`
- execute all scripts from:
  - `domains.sql`
  - `tables.sql`
  - `procedures.sql`

---

## 📤 Export Scripts

```
DbMetaTool export-scripts --connection-string "<fb-connection>" --output-dir "C:\out"
```

Produces:

```
domains.sql
tables.sql
procedures.sql
```

## 🔁 Update Database

```
DbMetaTool update-db --connection-string "<fb-connection>" --scripts-dir "C:\metadata"
```

Performs:

- missing domain detection + creation  
- missing table columns detection + ALTER TABLE ADD  
- CREATE OR ALTER PROCEDURE regeneration  

---

# 🧪 Testing

The project includes **integration tests** that run against **Firebird 5 Embedded**.

### Run all tests:

```
dotnet test
```

### What is tested?

#### ✔ Database creation
- reading metadata scripts  
- creating a fresh `.fdb`  
- verifying metadata presence  

#### ✔ Export correctness
- exported SQL matches actual database structure  
- correct parsing of domain types  
- procedures contain proper parameters & source  

#### ✔ Database updating
- adding missing domains  
- adding missing columns  
- recreating procedures  
- idempotency of operations  

---

# ⚙️ Requirements

- **.NET 8 SDK**  
- **Firebird 5 Embedded** (automatically downloaded via included script)  
- Windows x64 (Firebird 5 Embedded target)  

---

