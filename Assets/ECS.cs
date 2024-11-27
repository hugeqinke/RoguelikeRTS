using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
public static class EntityFactory
{
    public static void RegisterItem(GameObject obj)
    {
        var id = Simulator.Entity.RegisterNewId(obj);

        var components = obj.GetComponents<IComponent>();
        foreach (var component in components)
        {
            var componentType = component.GetType();
            Simulator.Entity.RegisterComponent(componentType, component, id);
        }
    }
}

public class Entity
{
    private Dictionary<System.Type, HashSet<uint>> _containers;
    private Dictionary<uint, Dictionary<System.Type, IComponent>> _components;

    private Dictionary<GameObject, uint> _idReferences;
    private Dictionary<uint, GameObject> _objectReferences;

    private uint _idIterator;

    public Entity()
    {
        _containers = new Dictionary<System.Type, HashSet<uint>>();
        _components = new Dictionary<uint, Dictionary<System.Type, IComponent>>();
        _idReferences = new Dictionary<GameObject, uint>();
        _objectReferences = new Dictionary<uint, GameObject>();
    }

    public GameObject GetObject(uint id)
    {
        return _objectReferences[id];
    }

    public uint GetId(GameObject obj)
    {
        return _idReferences[obj];
    }

    private void Load<T>() where T : MonoBehaviour, IComponent
    {
        var systemType = typeof(T);

        var components = GameObject.FindObjectsOfType(systemType).Cast<T>().ToList();
        var objects = components.Select(x => x.gameObject).ToList();

        var hashset = new HashSet<uint>();

        foreach (var obj in objects)
        {
            if (_idReferences.ContainsKey(obj))
            {
                hashset.Add(_idReferences[obj]);
            }
            else
            {
                hashset.Add(RegisterNewId(obj));
            }

            var id = _idReferences[obj];
            if (!_objectReferences.ContainsKey(id))
            {
                _objectReferences.Add(id, obj);
            }
        }

        _containers.Add(
            systemType,
            hashset
        );

        for (int i = 0; i < components.Count; i++)
        {
            var obj = objects[i];
            var component = components[i];

            var id = _idReferences[obj];
            if (!_components.ContainsKey(id))
            {
                _components.Add(id, new Dictionary<System.Type, IComponent>());
            }

            _components[id].Add(systemType, component);
        }

        foreach (var component in components)
        {

        }
    }

    public uint RegisterNewId(GameObject obj)
    {
        var id = _idIterator++;
        _idReferences[obj] = id;
        _objectReferences[id] = obj;
        _components.Add(id, new Dictionary<System.Type, IComponent>());

        return id;
    }

    public void RemoveEntity(GameObject obj)
    {
        var id = _idReferences[obj];
        foreach (var typeComponentKVP in _components[id])
        {
            _containers[typeComponentKVP.Key].Remove(id);
        }

        _components.Remove(id);

        _idReferences.Remove(obj);
        _objectReferences.Remove(id);
    }

    public void RegisterComponent(System.Type componentType, IComponent component, uint id)
    {
        _components[id].Add(componentType, component);

        if (!_containers.ContainsKey(componentType))
        {
            _containers.Add(componentType, new HashSet<uint>());
        }
        _containers[componentType].Add(id);

    }

    public void RegisterComponent<T>(T component, uint id) where T : IComponent
    {
        var componentType = typeof(T);
        RegisterComponent(componentType, component, id);
    }

    public T FetchComponent<T>(uint id)
    {
        return (T)_components[id][typeof(T)];
    }

    public T FetchComponent<T>(GameObject obj)
    {
        var id = _idReferences[obj];
        return (T)_components[id][typeof(T)];
    }

    public bool TryFetchComponent<T>(GameObject obj, out T component)
    {
        var id = _idReferences[obj];

        if (_components[id].TryGetValue(typeof(T), out IComponent fetchedComponent))
        {
            component = (T)fetchedComponent;
            return true;
        }
        component = default(T);
        return false;
    }

    public List<GameObject> Fetch(List<System.Type> componentTypes)
    {
        var results = new HashSet<uint>();
        var output = new List<GameObject>();
        if (componentTypes.Count > 0)
        {
            if (!_containers.ContainsKey(componentTypes[0]))
            {
                foreach (var result in results)
                {
                    var obj = _objectReferences[result];
                    output.Add(obj);
                }

                return output;
            }

            foreach (var id in _containers[componentTypes[0]])
            {
                results.Add(id);
            }
        }

        for (int i = 1; i < componentTypes.Count; i++)
        {
            var componentType = componentTypes[i];
            if (!_containers.ContainsKey(componentType))
            {
                continue;
            }

            var container = _containers[componentType];
            results.IntersectWith(container);
        }

        foreach (var result in results)
        {
            var obj = _objectReferences[result];
            output.Add(obj);
        }

        return output;
    }
}

public static class EntityExtensionMethods
{
    public static T FetchComponent<T>(this GameObject obj)
    {
        return Simulator.Entity.FetchComponent<T>(obj);
    }
}
