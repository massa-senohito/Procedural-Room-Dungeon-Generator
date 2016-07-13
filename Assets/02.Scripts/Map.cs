﻿using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

[System.Serializable]
public struct MinMax
{
	public int Min;
	public int Max;
}

public enum TileType
{
	Empty,
	Room,
	Corridor,
	Wall
}

public class Map : MonoBehaviour
{
	public Room RoomPrefab;
	public int RoomCount;
	public IntVector2 MapSize;
	public MinMax RoomSize;
	public float GenerationStepDelay;

	private List<Room> _rooms;
	private List<Corridor> _corridors;

	private TileType[,] _tilesTypes;

	public void SetTileType(IntVector2 coordinates, TileType tileType)
	{
		_tilesTypes[coordinates.x, coordinates.z] = tileType;
	}

	public TileType GetTileType(IntVector2 coordinates)
	{
		return _tilesTypes[coordinates.x, coordinates.z];
	}


	// Generate Rooms and Corridors
	public IEnumerator Generate()
	{
		_tilesTypes = new TileType[MapSize.x, MapSize.z];
		_rooms = new List<Room>();

		// Generate Rooms
		for (int i = 0; i < RoomCount; i++)
		{
			Room roomInstance = CreateRoom();
			if (roomInstance == null)
			{
				RoomCount = _rooms.Count;
				Debug.Log("Cannot make every rooms!");
				Debug.Log("Created Rooms : " + RoomCount);
				break;
			}
			StartCoroutine(roomInstance.Generate());
			yield return null;
		}

		yield return BowyerWatson();
		Debug.Log("Every Rooms are fully connected");

		yield return PrimMST();
		Debug.Log("Every Rooms are minimally connected");

		foreach (Corridor corridor in _corridors)
		{
			corridor.Show();
		}

		// Generate Corridors
		foreach (Corridor corridor in _corridors)
		{
			StartCoroutine(corridor.Generate());
			yield return null;
		}

		Debug.Log(_corridors.Count + " corridors remained");

		yield return WallCheck();

		foreach (Room room in _rooms)
		{
			yield return room.CreateWalls();
		}
	}

	private IEnumerator WallCheck()
	{
		for (int x = 0; x < MapSize.x; x++)
		{
			for (int z = 0; z < MapSize.z; z++)
			{
				if (_tilesTypes[x, z] == TileType.Empty && IsWall(x, z))
				{
					_tilesTypes[x, z] = TileType.Wall;
					Vector3 position = CoordinatesToPosition(new IntVector2(x, z));
					Vector3 half = new Vector3(0.5f, 0, 0.5f);
//					Debug.DrawLine(position - half, position + half, Color.red, 4f);
				}
			}
		}
		yield return null;
	}

	private bool IsWall(int x, int z)
	{
		for (int i = x - 1; i <= x + 1; i++)
		{
			if (i < 0 || i >= MapSize.x)
			{
				continue;
			}
			for (int j = z - 1; j <= z + 1; j++)
			{
				if (j < 0 || j >= MapSize.z || (i == x && j == z))
				{
					continue;
				}
				if (_tilesTypes[i, j] == TileType.Room || _tilesTypes[i, j] == TileType.Corridor)
				{
					return true;
				}
			}
		}

		return false;
	}

	private Room CreateRoom()
	{
		Room newRoom = null;

		// Try as many as we can.
		for (int i = 0; i < RoomCount * RoomCount; i++)
		{
			IntVector2 size = new IntVector2(Random.Range(RoomSize.Min, RoomSize.Max + 1), Random.Range(RoomSize.Min, RoomSize.Max + 1));
			IntVector2 coordinates = new IntVector2(Random.Range(1, MapSize.x - size.x), Random.Range(1, MapSize.z - size.z));
			if (!IsOverlapped(size, coordinates))
			{
				newRoom = Instantiate(RoomPrefab);
				_rooms.Add(newRoom);
				newRoom.Num = _rooms.Count;
				newRoom.name = "Room " + newRoom.Num + " (" + coordinates.x + ", " + coordinates.z + ")";
				newRoom.Size = size;
				newRoom.Coordinates = coordinates;
				newRoom.transform.parent = transform;
				Vector3 position = CoordinatesToPosition(coordinates);
				position.x += size.x * 0.5f - 0.5f; position.z += size.z * 0.5f - 0.5f;
				newRoom.transform.localPosition = position;
				newRoom.Init(this);
				break;
			}
		}

		if (newRoom == null)
		{
			Debug.LogError("Too many rooms in map!! : " + _rooms.Count);
		}

		return newRoom;
	}

	public IntVector2 RandomCoordinates
	{
		get
		{
			return new IntVector2(Random.Range(0, MapSize.x), Random.Range(0, MapSize.z));
		}
	}

	private bool IsOverlapped(IntVector2 size, IntVector2 coordinates)
	{
		foreach (Room room in _rooms)
		{
			// Give a little space between two rooms
			if (Mathf.Abs(room.Coordinates.x - coordinates.x + (room.Size.x - size.x) * 0.5f) < (room.Size.x + size.x) * 0.7f &&
				Mathf.Abs(room.Coordinates.z - coordinates.z + (room.Size.z - size.z) * 0.5f) < (room.Size.z + size.z) * 0.7f)
			{
				return true;
			}
		}
		return false;
	}

	// Big enough to cover the map
	private Triangle LootTriangle
	{
		get
		{
			Vector3[] vertexs = new Vector3[] {
			new Vector3(MapSize.x * 2, 0, MapSize.z),
			new Vector3(-MapSize.x * 2, 0, MapSize.z),
			new Vector3(0, 0, -2 * MapSize.z)};

			Room[] tempRooms = new Room[3];
			for (int i = 0; i < 3; i++)
			{
				tempRooms[i] = Instantiate(RoomPrefab);
				tempRooms[i].transform.localPosition = vertexs[i];
				tempRooms[i].name = "Loot Room " + i;
				tempRooms[i].Init(this);
			}

			return new Triangle(tempRooms[0], tempRooms[1], tempRooms[2]);
		}
	}

	private IEnumerator BowyerWatson()
	{
		List<Triangle> triangulation = new List<Triangle>();

		Triangle loot = LootTriangle;
		triangulation.Add(loot);

		foreach (Room room in _rooms)
		{
			List<Triangle> badTriangles = new List<Triangle>();

			foreach (Triangle triangle in triangulation)
			{
				if (triangle.IsContaining(room))
				{
					badTriangles.Add(triangle);
				}
			}

			List<Corridor> polygon = new List<Corridor>();
			foreach (Triangle badTriangle in badTriangles)
			{
				foreach (Corridor corridor in badTriangle.Corridors)
				{
					if (corridor.Triangles.Count == 1)
					{
						polygon.Add(corridor);
						corridor.Triangles.Remove(badTriangle);
						continue;
					}

					foreach (Triangle triangle in corridor.Triangles)
					{
						if (triangle == badTriangle)
						{
							continue;
						}

						// Delete Corridor which is between two bad triangles.
						if (badTriangles.Contains(triangle))
						{
							corridor.Rooms[0].RoomCorridor.Remove(corridor.Rooms[1]);
							corridor.Rooms[1].RoomCorridor.Remove(corridor.Rooms[0]);
							Destroy(corridor.gameObject);
						}
						else
						{
							polygon.Add(corridor);
						}
						break;
					}
				}
			}

			// Delete Bad Triangles
			for (int index = badTriangles.Count - 1; index >= 0; --index)
			{
				Triangle triangle = badTriangles[index];
				badTriangles.RemoveAt(index);
				triangulation.Remove(triangle);
				foreach (Corridor corridor in triangle.Corridors)
				{
					corridor.Triangles.Remove(triangle);
				}
			}

			foreach (Corridor corridor in polygon)
			{
				// TODO: Edge sync
				Triangle newTriangle = new Triangle(corridor.Rooms[0], corridor.Rooms[1], room);
				triangulation.Add(newTriangle);
			}

		}
		yield return null;

		for (int index = triangulation.Count - 1; index >= 0; index--)
		{
			if (triangulation[index].Rooms.Contains(loot.Rooms[0]) || triangulation[index].Rooms.Contains(loot.Rooms[1]) ||
				triangulation[index].Rooms.Contains(loot.Rooms[2]))
			{
				triangulation.RemoveAt(index);
			}
		}

		foreach (Room room in loot.Rooms)
		{
			List<Corridor> deleteList = new List<Corridor>();
			foreach (KeyValuePair<Room, Corridor> pair in room.RoomCorridor)
			{
				deleteList.Add(pair.Value);
			}
			for (int index = deleteList.Count - 1; index >= 0; index--)
			{
				Corridor corridor = deleteList[index];
				corridor.Rooms[0].RoomCorridor.Remove(corridor.Rooms[1]);
				corridor.Rooms[1].RoomCorridor.Remove(corridor.Rooms[0]);
				Destroy(corridor.gameObject);
			}
			Destroy(room.gameObject);
		}
	}

	private IEnumerator PrimMST()
	{
		List<Room> connectedRooms = new List<Room>();
		_corridors = new List<Corridor>();

		connectedRooms.Add(_rooms[0]);

		while (connectedRooms.Count < _rooms.Count)
		{
			KeyValuePair<Room, Corridor> minLength = new KeyValuePair<Room, Corridor>();
			List<Corridor> deleteList = new List<Corridor>();

			foreach (Room room in connectedRooms)
			{
				foreach (KeyValuePair<Room, Corridor> pair in room.RoomCorridor)
				{
					if (connectedRooms.Contains(pair.Key))
					{
						continue;
					}
					if (minLength.Value == null || minLength.Value.Length > pair.Value.Length)
					{
						minLength = pair;
					}
				}
			}

			// Check Unnecessary Corridors.
			foreach (KeyValuePair<Room, Corridor> pair in minLength.Key.RoomCorridor)
			{
				if (connectedRooms.Contains(pair.Key) && (minLength.Value != pair.Value))
				{
					deleteList.Add(pair.Value);
				}
			}

			// Delete corridors
			for (int index = deleteList.Count - 1; index >= 0; index--)
			{
				Corridor corridor = deleteList[index];
				corridor.Rooms[0].RoomCorridor.Remove(corridor.Rooms[1]);
				corridor.Rooms[1].RoomCorridor.Remove(corridor.Rooms[0]);
				deleteList.RemoveAt(index);
				Destroy(corridor.gameObject);
			}

			connectedRooms.Add(minLength.Key);
			_corridors.Add(minLength.Value);
		}
		yield return null;
	}

	public Vector3 CoordinatesToPosition(IntVector2 coordinates)
	{
		return new Vector3(coordinates.x - MapSize.x * 0.5f + 0.5f, 0f, coordinates.z - MapSize.z * 0.5f + 0.5f);
	}
}
