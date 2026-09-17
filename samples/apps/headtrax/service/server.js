#!/usr/bin/env node
// HeadTrax GraphQL Service
// Serves employee data from SQLite via Apollo Server 5 + Express.
//
// Usage:
//   npm start              # default port 4000, db = ./headtrax.db
//   PORT=3001 npm start    # custom port

import { readFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import express from "express";
import cors from "cors";
import { ApolloServer } from "@apollo/server";
import { expressMiddleware } from "@as-integrations/express4";
import Database from "better-sqlite3";

const __dirname = dirname(fileURLToPath(import.meta.url));
const DB_PATH = process.env.DB_PATH || join(__dirname, "headtrax.db");
const PORT = parseInt(process.env.PORT || "4000", 10);

// ── Database ────────────────────────────────────────────────────────────────

const db = new Database(DB_PATH, { readonly: true });
db.pragma("journal_mode = WAL");
db.pragma("cache_size = -64000"); // 64 MB cache

// ── GraphQL Schema ──────────────────────────────────────────────────────────

const typeDefs = `#graphql
  type Employee {
    id: Int!
    employeeNumber: String!
    firstName: String!
    lastName: String!
    email: String!
    phone: String
    title: String!
    department: String!
    location: String!
    hireDate: String!
    salary: Float!
    managerId: Int
    level: Int!
    status: String!
    birthDate: String
    gender: String
    performanceRating: Float
    stockOptions: Int!
    isRemote: Boolean!
    costCenter: String
    createdAt: String!
    updatedAt: String!
    manager: Employee
    directReports: [Employee!]!
    directReportCount: Int!
  }

  type EmployeePage {
    items: [Employee!]!
    totalCount: Int!
    continuationToken: String
  }

  input SortInput {
    field: String!
    direction: SortDirection!
  }

  enum SortDirection {
    ASC
    DESC
  }

  input FilterInput {
    field: String!
    operator: FilterOperator!
    value: String
    valueTo: String
  }

  enum FilterOperator {
    EQUALS
    NOT_EQUALS
    CONTAINS
    STARTS_WITH
    ENDS_WITH
    GREATER_THAN
    GREATER_THAN_OR_EQUAL
    LESS_THAN
    LESS_THAN_OR_EQUAL
    BETWEEN
    IN
    IS_NULL
    IS_NOT_NULL
  }

  type Query {
    employees(
      pageSize: Int = 50
      continuationToken: String
      sort: [SortInput!]
      filters: [FilterInput!]
      searchQuery: String
      select: [String!]
    ): EmployeePage!

    employee(id: Int!): Employee

    departments: [String!]!
    locations: [String!]!
    titles: [String!]!
    stats: DataStats!
  }

  type DataStats {
    totalEmployees: Int!
    activePct: Float!
    avgSalary: Float!
    departmentCount: Int!
    locationCount: Int!
  }
`;

// ── Column whitelist (prevents SQL injection) ───────────────────────────────

const ALLOWED_COLUMNS = new Set([
  "id", "employee_number", "first_name", "last_name", "email", "phone",
  "title", "department", "location", "hire_date", "salary", "manager_id",
  "level", "status", "birth_date", "gender", "performance_rating",
  "stock_options", "is_remote", "cost_center", "created_at", "updated_at",
]);

// Map camelCase GraphQL field names → snake_case SQL columns
const FIELD_MAP = {
  id: "id",
  employeeNumber: "employee_number",
  firstName: "first_name",
  lastName: "last_name",
  email: "email",
  phone: "phone",
  title: "title",
  department: "department",
  location: "location",
  hireDate: "hire_date",
  salary: "salary",
  managerId: "manager_id",
  level: "level",
  status: "status",
  birthDate: "birth_date",
  gender: "gender",
  performanceRating: "performance_rating",
  stockOptions: "stock_options",
  isRemote: "is_remote",
  costCenter: "cost_center",
  createdAt: "created_at",
  updatedAt: "updated_at",
};

function toDbColumn(graphqlField) {
  const col = FIELD_MAP[graphqlField] || graphqlField;
  if (!ALLOWED_COLUMNS.has(col)) throw new Error(`Invalid column: ${graphqlField}`);
  return col;
}

// ── Row mapping (snake_case → camelCase) ────────────────────────────────────

function mapRow(row) {
  if (!row) return null;
  return {
    id: row.id,
    employeeNumber: row.employee_number,
    firstName: row.first_name,
    lastName: row.last_name,
    email: row.email,
    phone: row.phone,
    title: row.title,
    department: row.department,
    location: row.location,
    hireDate: row.hire_date,
    salary: row.salary,
    managerId: row.manager_id,
    level: row.level,
    status: row.status,
    birthDate: row.birth_date,
    gender: row.gender,
    performanceRating: row.performance_rating,
    stockOptions: row.stock_options,
    isRemote: !!row.is_remote,
    costCenter: row.cost_center,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  };
}

// ── Query builder ───────────────────────────────────────────────────────────

const FILTER_OPS = {
  EQUALS:                (col) => `${col} = ?`,
  NOT_EQUALS:            (col) => `${col} != ?`,
  CONTAINS:              (col) => `${col} LIKE '%' || ? || '%'`,
  STARTS_WITH:           (col) => `${col} LIKE ? || '%'`,
  ENDS_WITH:             (col) => `${col} LIKE '%' || ?`,
  GREATER_THAN:          (col) => `${col} > ?`,
  GREATER_THAN_OR_EQUAL: (col) => `${col} >= ?`,
  LESS_THAN:             (col) => `${col} < ?`,
  LESS_THAN_OR_EQUAL:    (col) => `${col} <= ?`,
  BETWEEN:               (col) => `${col} BETWEEN ? AND ?`,
  IN:                    (col, count) => `${col} IN (${Array(count).fill("?").join(",")})`,
  IS_NULL:               (col) => `${col} IS NULL`,
  IS_NOT_NULL:           (col) => `${col} IS NOT NULL`,
};

function buildEmployeeQuery({ pageSize = 50, continuationToken, sort, filters, searchQuery, select }) {
  const params = [];
  const whereClauses = [];

  // FTS search
  if (searchQuery) {
    // Use FTS5 match with prefix search
    whereClauses.push("id IN (SELECT rowid FROM employees_fts WHERE employees_fts MATCH ?)");
    // Escape special FTS chars and add prefix matching
    const escaped = searchQuery.replace(/['"*()]/g, "").trim();
    params.push(`${escaped}*`);
  }

  // Filters
  if (filters && filters.length > 0) {
    for (const f of filters) {
      const col = toDbColumn(f.field);

      if (f.operator === "IS_NULL" || f.operator === "IS_NOT_NULL") {
        whereClauses.push(FILTER_OPS[f.operator](col));
      } else if (f.operator === "BETWEEN") {
        whereClauses.push(FILTER_OPS.BETWEEN(col));
        params.push(f.value, f.valueTo);
      } else if (f.operator === "IN") {
        const values = f.value.split(",").map(v => v.trim());
        whereClauses.push(FILTER_OPS.IN(col, values.length));
        params.push(...values);
      } else {
        whereClauses.push(FILTER_OPS[f.operator](col));
        params.push(f.value);
      }
    }
  }

  const whereSQL = whereClauses.length > 0 ? `WHERE ${whereClauses.join(" AND ")}` : "";

  // Count query
  const countSQL = `SELECT COUNT(*) as total FROM employees ${whereSQL}`;
  const totalCount = db.prepare(countSQL).get(...params).total;

  // Sort
  let orderSQL = "";
  if (sort && sort.length > 0) {
    const orderParts = sort.map(s => {
      const col = toDbColumn(s.field);
      const dir = s.direction === "DESC" ? "DESC" : "ASC";
      return `${col} ${dir}`;
    });
    orderSQL = `ORDER BY ${orderParts.join(", ")}`;
  } else {
    orderSQL = "ORDER BY id ASC";
  }

  // Pagination (continuation token = offset)
  const offset = continuationToken ? parseInt(continuationToken, 10) : 0;

  // Column projection
  let selectCols = "*";
  if (select && select.length > 0) {
    const cols = select.map(toDbColumn);
    // Always include id for keying
    if (!cols.includes("id")) cols.unshift("id");
    selectCols = cols.join(", ");
  }

  const dataSQL = `SELECT ${selectCols} FROM employees ${whereSQL} ${orderSQL} LIMIT ? OFFSET ?`;
  const dataParams = [...params, pageSize, offset];
  const rows = db.prepare(dataSQL).all(...dataParams);

  const nextOffset = offset + rows.length;
  const nextToken = nextOffset < totalCount ? String(nextOffset) : null;

  return {
    items: rows.map(mapRow),
    totalCount,
    continuationToken: nextToken,
  };
}

// ── Prepared statements for common lookups ──────────────────────────────────

const stmtGetById = db.prepare("SELECT * FROM employees WHERE id = ?");
const stmtGetManager = db.prepare("SELECT * FROM employees WHERE id = ?");
const stmtGetDirectReports = db.prepare("SELECT * FROM employees WHERE manager_id = ?");
const stmtDirectReportCount = db.prepare("SELECT COUNT(*) as cnt FROM employees WHERE manager_id = ?");
const stmtDepartments = db.prepare("SELECT DISTINCT department FROM employees ORDER BY department");
const stmtLocations = db.prepare("SELECT DISTINCT location FROM employees ORDER BY location");
const stmtTitles = db.prepare("SELECT DISTINCT title FROM employees ORDER BY title");
const stmtStats = db.prepare(`
  SELECT
    COUNT(*) as totalEmployees,
    ROUND(100.0 * SUM(CASE WHEN status = 'Active' THEN 1 ELSE 0 END) / COUNT(*), 1) as activePct,
    ROUND(AVG(salary), 0) as avgSalary,
    COUNT(DISTINCT department) as departmentCount,
    COUNT(DISTINCT location) as locationCount
  FROM employees
`);

// ── Resolvers ───────────────────────────────────────────────────────────────

const resolvers = {
  Query: {
    employees: (_, args) => buildEmployeeQuery(args),
    employee: (_, { id }) => mapRow(stmtGetById.get(id)),
    departments: () => stmtDepartments.all().map(r => r.department),
    locations: () => stmtLocations.all().map(r => r.location),
    titles: () => stmtTitles.all().map(r => r.title),
    stats: () => stmtStats.get(),
  },
  Employee: {
    manager: (emp) => emp.managerId ? mapRow(stmtGetManager.get(emp.managerId)) : null,
    directReports: (emp) => stmtGetDirectReports.all(emp.id).map(mapRow),
    directReportCount: (emp) => stmtDirectReportCount.get(emp.id).cnt,
  },
};

// ── Server ──────────────────────────────────────────────────────────────────

async function start() {
  const server = new ApolloServer({ typeDefs, resolvers });
  await server.start();

  const app = express();
  app.use(cors());

  // Health check
  app.get("/health", (_, res) => res.json({ ok: true, db: DB_PATH }));

  // GraphQL endpoint
  app.use("/graphql", express.json(), expressMiddleware(server));

  app.listen(PORT, () => {
    const total = db.prepare("SELECT COUNT(*) as cnt FROM employees").get();
    console.log(`\n🚀 HeadTrax GraphQL service`);
    console.log(`   Endpoint:  http://localhost:${PORT}/graphql`);
    console.log(`   Database:  ${DB_PATH}`);
    console.log(`   Employees: ${total.cnt.toLocaleString()}`);
    console.log(`   Sandbox:   http://localhost:${PORT}/graphql\n`);
  });
}

start().catch((err) => {
  console.error("Failed to start server:", err);
  process.exit(1);
});
