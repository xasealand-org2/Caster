﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CaterCommon;
using CaterModel;

namespace CaterDal
{
    /// <summary>
    /// 订单 数据层
    /// </summary>
    public partial class OrderInfoDal
    {
        /// <summary>
        /// 开单
        /// </summary>
        /// <param name="tableId">桌号</param>
        /// <returns></returns>
        public int AddOrder(int tableId)
        {
            //插入订单数据
            //更新餐桌状态
            //写在一起执行，只需要和数据库交互一次
            //下订单
            string sql = "INSERT INTO OrderInfo(odate,ispay,tableId) VALUES(DATETIME('now', 'localtime'),0,@tid);" +
                //更新餐桌状态
                "UPDATE TableInfo SET tIsFree=0 WHERE tid=@tid;" +
                //获取最新的订单编号
                "SELECT oid FROM orderinfo ORDER BY oid DESC LIMIT 0,1";
            SqlParameter p=new SqlParameter("@tid",tableId);
            return Convert.ToInt32(SQLHelper.ExecuteScalar(sql, p));
        }

        /// <summary>
        /// 通过桌号得到订单Id
        /// </summary>
        /// <param name="tableId"></param>
        /// <returns></returns>
        public int GetOrderIdByTableId(int tableId)
        {
            string sql = "SELECT oid FROM orderinfo where tableId=@tableid and ispay=0";
            SqlParameter p=new SqlParameter("@tableId",tableId);
            return Convert.ToInt32(SQLHelper.ExecuteScalar(sql, p));
        }

        /// <summary>
        /// 点菜
        /// </summary>
        /// <param name="orderid">订单Id</param>
        /// <param name="dishId">菜肴Id</param>
        /// <returns></returns>
        public int OrderDishes(int orderid, int dishId)
        {
            //查询当前订单是否已经点了这道菜
            string sql = "SELECT COUNT(*) FROM orderDetailInfo WHERE orderId=@oid AND dishId=@did";
            SqlParameter[] ps =
            {
                new SqlParameter("@oid", orderid),
                new SqlParameter("@did", dishId)
            };
            int count = Convert.ToInt32(SQLHelper.ExecuteScalar(sql, ps));
            if (count > 0)
            {
                //这个订单已经点过这个菜，让数量加1
                sql = "UPDATE orderDetailInfo SET count=count+1 WHERE orderId=@oid AND dishId=@did";
            }
            else
            {
                //当前订单还没有点这个菜，加入这个菜
                sql = "INSERT INTO orderDetailInfo(orderid,dishId,count) VALUES(@oid,@did,1)";
            }
            return SQLHelper.ExecuteNonQuery(sql, ps);
        }

        public int UpdateCountByOId(int oid,int count)
        {
            string sql = "UPDATE orderDetailInfo SET count=@count where oid=@oid";
            SqlParameter[] ps =
            {
                new SqlParameter("@count", count),
                new SqlParameter("@oid", oid)
            };
            return SQLHelper.ExecuteNonQuery(sql, ps);
        }

        public List<OrderDetailInfo> GetDetailList(int orderId)
        {
            string sql=@"SELECT odi.oid,di.dTitle,di.dPrice,odi.count FROM dishinfo AS di
            INNER JOIN OrderDetailInfo AS odi
            ON di.did=odi.dishid
            WHERE odi.orderId=@orderid";
            SqlParameter p=new SqlParameter("@orderid",orderId);

            DataTable dt = SQLHelper.GetDataTable(sql, p);
            List<OrderDetailInfo> list=new List<OrderDetailInfo>();

            foreach (DataRow row in dt.Rows)
            {
                list.Add(new OrderDetailInfo()
                {
                    Id = Convert.ToInt32(row["oid"]),
                    DTitle = row["dtitle"].ToString(),
                    DPrice = Convert.ToDecimal(row["dprice"]),
                    Count = Convert.ToInt32(row["count"])
                });
            }

            return list;
        }

        public decimal GetTotalMoneyByOrderId(int orderid)
        {
            string sql = @"	SELECT SUM(oti.count*di.dprice) 
	            FROM orderdetailinfo AS oti
	            INNER JOIN dishinfo AS di
	            ON oti.dishid=di.did
	            WHERE oti.orderid=@orderid";
           SqlParameter p=new SqlParameter("@orderid",orderid);

            object obj = SQLHelper.ExecuteScalar(sql, p);
            if (obj == DBNull.Value)
            {
                return 0;
            }
            return Convert.ToDecimal(obj);
        }

        public int SetOrderMomey(int orderid,decimal money)
        {
            string sql = "UPDATE orderinfo set omoney=@money WHERE oid=@oid";
            SqlParameter[] ps =
            {
                new SqlParameter("@money", money),
                new SqlParameter("@oid", orderid)
            };
            return SQLHelper.ExecuteNonQuery(sql, ps);
        }

        public int DeleteDetailById(int oid)
        {
            string sql = "DELETE FROM orderDetailInfo WHERE oid=@oid";
            SqlParameter p=new SqlParameter("@oid",oid);
            return SQLHelper.ExecuteNonQuery(sql, p);
        }

        public int Pay(bool isUseMoney,int memberId,decimal payMoney,int orderid,decimal discount)
        {
            //创建数据库的链接对象
            using (SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["itcastCater"].ConnectionString))
            {
                int result = 0;
                //由数据库链接对象创建事务
                conn.Open();
                SqlTransaction tran = conn.BeginTransaction();

                //创建command对象
                SqlCommand cmd=new SqlCommand();
                //将命令对象启用事务
                cmd.Transaction = tran;
                //执行各命令
                string sql = "";
                SqlParameter[] ps;
                try
                {
                    //1、根据是否使用余额决定扣款方式
                    if (isUseMoney)
                    {
                        //使用余额
                        sql = "UPDATE MemberInfo SET mMoney=mMoney-@payMoney WHERE mid=@mid";
                        ps = new SqlParameter[]
                        {
                            new SqlParameter("@payMoney", payMoney),
                            new SqlParameter("@mid", memberId)
                        };
                        cmd.CommandText = sql;
                        cmd.Parameters.AddRange(ps);
                        result+=cmd.ExecuteNonQuery();
                    }

                    //2、将订单状态为IsPage=1
                    sql = "UPDATE orderInfo SET isPay=1,memberId=@mid,discount=@discount WHERE oid=@oid";
                    ps = new SqlParameter[]
                    {
                        new SqlParameter("@mid", memberId),
                        new SqlParameter("@discount", discount),
                        new SqlParameter("@oid", orderid)
                    };
                    cmd.CommandText = sql;
                    cmd.Parameters.Clear();
                    cmd.Parameters.AddRange(ps);
                    result += cmd.ExecuteNonQuery();

                    //3、将餐桌状态IsFree=1
                    sql = "UPDATE tableInfo SET tIsFree=1 WHERE tid=(SELECT tableId FROM orderinfo WHERE oid=@oid)";
                    SqlParameter p = new SqlParameter("@oid", orderid);
                    cmd.CommandText = sql;
                    cmd.Parameters.Clear();
                    cmd.Parameters.Add(p);
                    result += cmd.ExecuteNonQuery();
                    //提交事务
                    tran.Commit();
                }
                catch
                {
                    result = 0;
                    //回滚事务
                    tran.Rollback();
                }
                return result;
            }
        }
    }
}
