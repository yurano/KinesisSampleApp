﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Configuration;
using System.Threading;

using Amazon;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Amazon.Runtime;

namespace KinesisSampleApp
{
	public class KinesisModel
	{
		#region >>> private フィールド <<<

		/// <summary>
		/// Kinesisに送信するレコードのスレッドセーフなコレクション
		/// </summary>
		private readonly BlockingCollection<PutRecordsRequestEntry> _records;

		/// <summary>
		/// 一度に送信する最大レコード数
		/// </summary>
		private const int MaxRecordsRequestSize = 500;
		
		/// <summary>
		/// Name of the Kinesis stream.
		/// </summary>
		private const string StreamName = "TEST";

		/// <summary>
		/// An error message that is displayed if the records list is disposed.
		/// </summary>
		private const string RecordsListDisposed = "送信待ちレコードのコレクションが破棄または変更されたため、送信を中止しました。";

		/// <summary>
		/// Time interval to call PutRecords method.
		/// </summary>
		private const int PutRecordsInterval = 1000;
		#endregion

		#region >>> コンストラクター <<<

		/// <summary>
		/// Amazon Kinesis上の処理を行うKinesisModelのインスタンスを生成します。
		/// </summary>
		/// <param name="records">Kinesisに送信するレコードのコレクションを指定します。</param>
		public KinesisModel( BlockingCollection<PutRecordsRequestEntry> records )
		{
			_records = records;
			Client = new AmazonKinesisClient( AppConfig.AccessKeyId, AppConfig.SecretAccesskey, RegionEndpoint.APNortheast1 );
		}

		#endregion

		#region >>> public メソッド <<<

		/// <summary>
		/// レコードのコレクションを監視して送信するレコードがあればKinesisにPutします。
		/// このメソッドはスレッドセーフです。
		/// </summary>
		public void Consume()
		{
			// 監視対象のレコードがnullなら処理を抜ける。
			if( _records == null )
			{
				Debug.WriteLine( RecordsListDisposed );
				return;
			}

			// レコードのコレクションが空で、これ以上の追加がない限りそPutRecords処理を繰り返す。
			try
			{
				while( !_records.IsCompleted )
				{
					PutRecords();
					Thread.Sleep( PutRecordsInterval );
				}
			}
			catch( ObjectDisposedException )
			{
				Debug.WriteLine( RecordsListDisposed );
				// レコードのコレクションが破棄されていた場合は処理を抜ける。
				return;
			}
		}

		#endregion

		#region >>> private メソッド <<<
		
		/// <summary>
		/// レコードのコレクションを監視して送信するレコードがあればKinesisにPutします。
		/// このメソッドはスレッドセーフです。
		/// </summary>
		private void PutRecords()
		{
			// KinesisClientのPutRecordsメソッドに渡すリクエストを初期化
			PutRecordsRequest req = new PutRecordsRequest();
			req.Records = new List<PutRecordsRequestEntry>();
			
			int loopcnt = 0;

			// 一回に送信する最大のサイズになるまでレコードをリクエストに追加
			while( loopcnt < MaxRecordsRequestSize )
			{
				PutRecordsRequestEntry entry;
				
				// Producerがレコードを追加したコレクションから、レコードの取得を試みる。
				// Timeoutは仮で1000msだが後にパラメーター化する。
				try
				{
					if( _records.TryTake( out entry, 1000 ) )
					{
						req.Records.Add( entry );
						loopcnt++;
					}
					else
					{
						// レコードを取得できなかったらループを抜ける。
						break;
					}
				}
				catch( Exception e )
				{
					// 送信待ちレコードのコレクションが変更または破棄された場合は処理を抜ける。
					if( e is ObjectDisposedException
						|| e is InvalidOperationException )
					{
						Debug.WriteLine( "=================================送信時の例外" );
						Debug.WriteLine( RecordsListDisposed );
						break;
					}
					else
					{
						throw e;
					}
				}
			}

			// 送信リクエストにレコードが1つ以上あれば、リクエストを送信。
			if( req.Records.Count >= 1 )
			{
				req.StreamName = StreamName;
				
				// コールバックメソッドにPutRecordsCallBackを指定してPutRecordsを非同期実行する。
				Client.BeginPutRecords( req, (result)=>PutRecordsCallBack(result), null );
				
			}

		}
		
		/// <summary>
		/// KinesisへPutRecordsを非同期実行した際のコールバックメソッドです。
		/// </summary>
		/// <param name="result">PutRecordsの結果</param>
		private void PutRecordsCallBack( IAsyncResult result )
		{
			try
			{
				var re = Client.EndPutRecords( result );
				// Putに失敗したレコードがあるか
				if( re.FailedRecordCount > 0 )
				{
					// シーケンスナンバーが空のレコードリストを取得
					var failedRecords = from x in re.Records
										where String.IsNullOrEmpty( x.SequenceNumber )
										select x;

					foreach( var record in failedRecords )
					{
						// エラー内容を調査するコードをここに追加する予定。
						Debug.WriteLine( "{0}:{1}",record.ErrorCode, record.ErrorMessage );
					}
				}
			}
			catch( AmazonKinesisException e )
			{
				Debug.WriteLine( "コールバックメソッドでの例外" );
				Debug.WriteLine( e.Message );
				Debug.WriteLine( e.InnerException.Message );
			}
		}

		#endregion

		#region >>> プロパティ <<<

		/// <summary>
		/// AmazonKinesisのサービスにアクセスするクライアントオブジェクトを取得または設定します。
		/// </summary>
		private AmazonKinesisClient Client
		{
			get;
			set;
		}

		#endregion
	}
}
