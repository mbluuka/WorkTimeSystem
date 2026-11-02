// Bridge to the SDK implementation
import {
  createEvent as sdkCreateEvent,
  getOutbox as sdkGetOutbox,
  syncOutbox as sdkSyncOutbox,
  updateOutboxEvent as sdkUpdateOutboxEvent,
  deleteOutboxEvent as sdkDeleteOutboxEvent
} from '../../WorkTime.ClientSdk/pwa-client.js';
import { getToken } from './api';

export async function createEvent(params){
  return sdkCreateEvent(params);
}

export async function getOutbox(){
  return sdkGetOutbox();
}

// Корректировка события, которое ещё не ушло на сервер (офлайн-очередь).
export async function updateOutboxEvent(event){
  return sdkUpdateOutboxEvent(event);
}

export async function deleteOutboxEvent(id){
  return sdkDeleteOutboxEvent(id);
}

export async function syncOutbox(opts){
  const token = getToken();
  return sdkSyncOutbox({ ...opts, token });
}