using System;
using Android.Bluetooth;
using MvvmCross.Platform;
using MvvmCross.Platform.Platform;
using MvvmCross.Plugins.BLE.Bluetooth.LE;

namespace MvvmCross.Plugins.BLE.Droid.Bluetooth.LE
{
    public interface IGattCallback
    {
        event EventHandler<ServicesDiscoveredEventArgs> ServicesDiscovered;
        event EventHandler<CharacteristicReadEventArgs> CharacteristicValueUpdated;
        event EventHandler<CharacteristicWriteEventArgs> CharacteristicValueWritten;
    }

    public partial class Adapter : BluetoothGattCallback, IGattCallback
    {
        public event EventHandler<ServicesDiscoveredEventArgs> ServicesDiscovered = delegate { };
        public event EventHandler<CharacteristicReadEventArgs> CharacteristicValueUpdated = delegate { };
        public event EventHandler<CharacteristicWriteEventArgs> CharacteristicValueWritten = delegate { };

        public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            base.OnConnectionStateChange(gatt, status, newState);
            IDevice device = null;

            if (status != GattStatus.Success)
            {
                Mvx.TaggedError("OnConnectionStateChange", "GattCallback error: {0}", status);
                DeviceConnectionError(this, new DeviceConnectionEventArgs(){ Device = device });
                // We don't return. Allowing to fall-through to the SWITCH, which will assume a disconnect, close GATT and clean up.
                // The above error event handles the case where the error happened during a Connect call, which will close out any waiting asyncs.
            }
            else
            {
                Mvx.Trace("GattCallback state: {0}", newState.ToString());
            }
            
            switch (newState)
            {
                // disconnected
                case ProfileState.Disconnected:

                    if (DeviceOperationRegistry.TryGetValue(gatt.Device.Address, out device))
                    {
                        Mvx.Trace("Disconnected by user");

                        //Found so we can remove it
                        DeviceOperationRegistry.Remove(gatt.Device.Address);
                        ConnectedDeviceRegistry.Remove(gatt.Device.Address);
                        gatt.Close();

                        DeviceDisconnected(this, new DeviceConnectionEventArgs { Device = device });
                        break;
                    }

                    //connection must have been lost, bacause our device was not found in the registry but was still connected
                    if (ConnectedDeviceRegistry.TryGetValue(gatt.Device.Address, out device))
                    {
                        Mvx.Trace("Disconnected by lost connection");

                        ConnectedDeviceRegistry.Remove(gatt.Device.Address);
                        gatt.Close();

                        DeviceConnectionLost(this, new DeviceConnectionEventArgs() { Device = device });
                        break;
                    }

                    gatt.Close(); // Close GATT regardless, else we can accumulate zombie gatts.
                    Mvx.Trace("Disconnect. Device not found in registry. Not raising disconnect/lost event.");

                    break;
                // connecting
                case ProfileState.Connecting:
                    Mvx.Trace("Connecting");
                    break;
                // connected
                case ProfileState.Connected:
                    Mvx.Trace("Connected");

                    //Try to find the device in the registry so that the same instance is updated
                    if (DeviceOperationRegistry.TryGetValue(gatt.Device.Address, out device))
                    {
                        ((Device)device).Update(gatt.Device, gatt, this);

                        //Found so we can remove it
                        DeviceOperationRegistry.Remove(gatt.Device.Address);
                    }
                    else
                    {
                        //only for on auto-reconnect (device is not in operation registry)
                        device = new Device(gatt.Device, gatt, this, 0);
                    }

                    ConnectedDeviceRegistry[gatt.Device.Address] = device;
                    DeviceConnected(this, new DeviceConnectionEventArgs() { Device = device });

                    break;
                // disconnecting
                case ProfileState.Disconnecting:
                    Mvx.Trace("Disconnecting");
                    break;
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
        {
            base.OnServicesDiscovered(gatt, status);

            Mvx.Trace("OnServicesDiscovered: " + status.ToString());

            this.ServicesDiscovered(this, new ServicesDiscoveredEventArgs());
        }

        public override void OnDescriptorRead(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status)
        {
            base.OnDescriptorRead(gatt, descriptor, status);

            Mvx.Trace("OnDescriptorRead: " + descriptor.ToString());

        }

        public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            base.OnCharacteristicRead(gatt, characteristic, status);

            Mvx.Trace("OnCharacteristicRead: id {0}; status {1}", characteristic.Uuid, status);

            this.CharacteristicValueUpdated(this, new CharacteristicReadEventArgs
            {
                // memory leak ... used null params
                // dummy device with null gatt/gattcalback
                Characteristic = new Characteristic(characteristic, null, null)
            }
            );
        }

        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            base.OnCharacteristicChanged(gatt, characteristic);

            //Console.WriteLine("OnCharacteristicChanged: " + characteristic.GetStringValue(0));

            this.CharacteristicValueUpdated(this, new CharacteristicReadEventArgs()
            {
                // use null to avoid huge memory leaks due to characterisitc events
                Characteristic = new Characteristic(characteristic, null, null)
            }
            );
        }

        public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            base.OnCharacteristicWrite(gatt, characteristic, status);

            Mvx.Trace("OnCharacteristicWrite: {0}", status);

            var args = new CharacteristicWriteEventArgs() { Characteristic = new Characteristic(characteristic, null, null) };
            switch (status)
            {
                case GattStatus.Failure:
                case GattStatus.InsufficientAuthentication:
                case GattStatus.InsufficientEncryption:
                case GattStatus.InvalidAttributeLength:
                case GattStatus.InvalidOffset:
                case GattStatus.ReadNotPermitted:
                case GattStatus.RequestNotSupported:
                case GattStatus.WriteNotPermitted:
                    args.IsSuccessful = false;
                    break;
                case GattStatus.Success:
                    args.IsSuccessful = true;
                    break;
            }

            CharacteristicValueWritten(this, args);
        }

        public override void OnReliableWriteCompleted(BluetoothGatt gatt, GattStatus status)
        {
            base.OnReliableWriteCompleted(gatt, status);

            Mvx.Trace("OnReliableWriteCompleted: {0}", status);
        }
    }


}

