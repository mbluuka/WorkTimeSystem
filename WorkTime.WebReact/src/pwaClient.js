// Bridge to the SDK implementation
import { createEvent as sdkCreateEvent, getOutbox as sdkGetOutbox, syncOutbox as sdkSyncOutbox } from '../../WorkTime.ClientSdk/pwa-client.js';
import { getToken } from './api';

export async function createEvent(params){
  return sdkCreateEvent(params);
}

export async function getOutbox(){
  return sdkGetOutbox();
}

export async function syncOutbox(opts){
  const token = getToken();
  return sdkSyncOutbox({ ...opts, token });
}