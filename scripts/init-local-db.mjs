import fs from 'node:fs/promises';
import path from 'node:path';
import { randomBytes } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import pg from '../.local/node_modules/pg/lib/index.js';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const envPath = path.join(root, '.local', 'db-env.json');
try {
  await fs.access(envPath);
  console.log('Local database is already initialized.');
  process.exit(0);
} catch {}

const password = () => randomBytes(32).toString('hex');
const adminPassword = password();
const commandPassword = password();
const queryPassword = password();
const base = { host: '127.0.0.1', port: 55432, user: 'booking_owner' };
const client = new pg.Client({ ...base, database: 'postgres' });
await client.connect();
await client.query('CREATE DATABASE booking');
await client.end();
const database = new pg.Client({ ...base, database: 'booking' });
await database.connect();
let schema = await fs.readFile(path.join(root, 'database', '001-init.sql'), 'utf8');
schema = schema.replace(/^\\set.*\r?\n/gm, '')
  .replaceAll(":'command_password'", `'${commandPassword}'`)
  .replaceAll(":'query_password'", `'${queryPassword}'`);
await database.query(schema);
await database.query(`ALTER ROLE booking_owner PASSWORD '${adminPassword}'`);
await database.end();
const connection = (user, secret) => `Host=127.0.0.1;Port=55432;Database=booking;Username=${user};Password=${secret};Timeout=5;Command Timeout=10`;
await fs.writeFile(envPath, JSON.stringify({
  TEST_DATABASE_CONNECTION: connection('booking_owner', adminPassword),
  TEST_COMMAND_CONNECTION: connection('booking_command', commandPassword),
  TEST_QUERY_CONNECTION: connection('booking_query', queryPassword),
  BOOKING_TOKEN_SECRET: password(),
}, null, 2));
console.log('Local test schema and restricted database roles initialized.');
